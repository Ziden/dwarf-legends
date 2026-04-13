using System;
using DwarfFortress.GameLogic.Entities.Components;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal enum DwarfSpriteFacing
{
    Left,
    Right,
}

internal enum DwarfSpriteActionKind
{
    Idle,
    Walk,
    Mine,
    Chop,
    Craft,
    Gather,
    Build,
    Combat,
}

internal readonly record struct DwarfSpritePose(DwarfSpriteFacing Facing, DwarfSpriteActionKind Action, int Frame)
{
    public static DwarfSpritePose Idle(DwarfSpriteFacing facing = DwarfSpriteFacing.Right)
        => new(facing, DwarfSpriteActionKind.Idle, 0);
}

internal static class DwarfSpriteComposer
{
    private const int CanvasSize = PixelArtFactory.Size;
    private const int PixelScale = 2;
    private const int OutlineThickness = 1;
    private const float LogicalCanvasSize = CanvasSize / 2f;
    private const float CarryHoldFrontHandBias = 0.82f;
    private static readonly Vector2 CarryHoldPixelOffset = new(-0.10f, -0.55f);

    private enum DwarfToolKind
    {
        None,
        Pickaxe,
        Axe,
        Hammer,
        Sword,
    }

    private readonly record struct DwarfPalette(
        Color Tunic,
        Color TunicShadow,
        Color TunicHighlight,
        Color Skin,
        Color SkinShadow,
        Color SkinHighlight,
        Color Hair,
        Color HairShadow,
        Color Beard,
        Color BeardShadow,
        Color Belt,
        Color Boots,
        Color Pants,
        Color Metal,
        Color Outline);

    private readonly record struct LimbPose(Vector2I Shoulder, Vector2I Elbow, Vector2I Hand);

    private readonly record struct LegPose(Vector2I Hip, Vector2I Knee, Vector2I Foot);

    private readonly record struct DwarfFrameSpec(
        Rect2I Head,
        Rect2I Torso,
        LimbPose BackArm,
        LimbPose FrontArm,
        LegPose BackLeg,
        LegPose FrontLeg,
        DwarfToolKind ToolKind,
        Vector2I ToolStart,
        Vector2I ToolEnd);

    public static int GetFrameCount(DwarfSpriteActionKind action)
        => action switch
        {
            DwarfSpriteActionKind.Walk => 2,
            DwarfSpriteActionKind.Mine => 3,
            DwarfSpriteActionKind.Chop => 3,
            DwarfSpriteActionKind.Build => 3,
            DwarfSpriteActionKind.Combat => 3,
            DwarfSpriteActionKind.Craft => 2,
            DwarfSpriteActionKind.Gather => 2,
            _ => 1,
        };

    public static float GetFrameDurationSeconds(DwarfSpriteActionKind action)
        => action switch
        {
            DwarfSpriteActionKind.Craft => 0.22f,
            DwarfSpriteActionKind.Gather => 0.24f,
            DwarfSpriteActionKind.Build => 0.18f,
            DwarfSpriteActionKind.Combat => 0.14f,
            _ => 0.16f,
        };

    public static Texture2D Create(DwarfAppearanceComponent appearance, DwarfSpritePose pose)
    {
        var normalizedPose = NormalizePose(pose);
        var image = NewImage();
        var palette = ResolvePalette(appearance);
        var frame = ResolveFrame(normalizedPose);

        DrawBackLeg(image, frame.BackLeg, palette);
        DrawBackArm(image, frame.BackArm, palette);
        DrawTorso(image, frame.Torso, palette);
        DrawHead(image, frame.Head, appearance, palette);
        DrawFrontLeg(image, frame.FrontLeg, palette);
        DrawFrontArm(image, frame.FrontArm, palette);
        DrawTool(image, frame.ToolKind, frame.ToolStart, frame.ToolEnd, palette);

        if (normalizedPose.Facing == DwarfSpriteFacing.Left)
            image.FlipX();

        OutlineOpaqueSilhouette(image, palette.Outline, OutlineThickness);
        return ImageTexture.CreateFromImage(image);
    }

    internal static Vector2 ResolveCarryAnchorOffset(DwarfSpritePose pose, Vector2 spriteWorldSize)
    {
        var normalizedPose = NormalizePose(pose);
        var holdPoint = ResolveCarryHoldPoint(ResolveFrame(normalizedPose));
        var mirroredX = normalizedPose.Facing == DwarfSpriteFacing.Left
            ? LogicalCanvasSize - holdPoint.X
            : holdPoint.X;

        return new Vector2(
            ((mirroredX / LogicalCanvasSize) - 0.5f) * spriteWorldSize.X,
            (holdPoint.Y / LogicalCanvasSize) * spriteWorldSize.Y);
    }

    private static DwarfSpritePose NormalizePose(DwarfSpritePose pose)
    {
        var frameCount = GetFrameCount(pose.Action);
        if (frameCount <= 1)
            return new DwarfSpritePose(pose.Facing, pose.Action, 0);

        var normalizedFrame = ((pose.Frame % frameCount) + frameCount) % frameCount;
        return new DwarfSpritePose(pose.Facing, pose.Action, normalizedFrame);
    }

    private static DwarfPalette ResolvePalette(DwarfAppearanceComponent appearance)
    {
        var seed = StableHash(appearance.Signature);
        var tunics = new[]
        {
            new Color(0.57f, 0.22f, 0.18f),
            new Color(0.22f, 0.40f, 0.44f),
            new Color(0.34f, 0.48f, 0.24f),
            new Color(0.31f, 0.33f, 0.51f),
            new Color(0.61f, 0.49f, 0.20f),
            new Color(0.45f, 0.25f, 0.42f),
        };
        var skinTones = new[]
        {
            new Color(0.96f, 0.84f, 0.67f),
            new Color(0.90f, 0.76f, 0.58f),
            new Color(0.84f, 0.66f, 0.50f),
            new Color(0.72f, 0.54f, 0.38f),
        };

        var tunic = tunics[(seed & int.MaxValue) % tunics.Length];
        var skin = skinTones[((seed >> 3) & int.MaxValue) % skinTones.Length];
        var hair = ResolveHairColor(appearance.HairColor);
        var beard = ResolveHairColor(appearance.BeardColor);

        return new DwarfPalette(
            Tunic: tunic,
            TunicShadow: tunic.Darkened(0.20f),
            TunicHighlight: tunic.Lightened(0.18f),
            Skin: skin,
            SkinShadow: skin.Darkened(0.16f),
            SkinHighlight: skin.Lightened(0.12f),
            Hair: hair,
            HairShadow: hair.Darkened(0.20f),
            Beard: beard,
            BeardShadow: beard.Darkened(0.22f),
            Belt: new Color(0.44f, 0.31f, 0.16f),
            Boots: new Color(0.21f, 0.14f, 0.09f),
            Pants: tunic.Darkened(0.30f),
            Metal: new Color(0.76f, 0.78f, 0.84f),
            Outline: new Color(0.09f, 0.07f, 0.06f, 0.96f));
    }

    private static DwarfFrameSpec ResolveFrame(DwarfSpritePose pose)
        => pose.Action switch
        {
            DwarfSpriteActionKind.Walk => ResolveWalkFrame(pose.Frame),
            DwarfSpriteActionKind.Mine => ResolveSwingFrame(DwarfToolKind.Pickaxe, pose.Frame),
            DwarfSpriteActionKind.Chop => ResolveSwingFrame(DwarfToolKind.Axe, pose.Frame),
            DwarfSpriteActionKind.Build => ResolveSwingFrame(DwarfToolKind.Hammer, pose.Frame),
            DwarfSpriteActionKind.Combat => ResolveSwingFrame(DwarfToolKind.Sword, pose.Frame),
            DwarfSpriteActionKind.Craft => ResolveCraftFrame(pose.Frame),
            DwarfSpriteActionKind.Gather => ResolveGatherFrame(pose.Frame),
            _ => ResolveIdleFrame(),
        };

    private static DwarfFrameSpec ResolveIdleFrame()
        => new(
            Head: new Rect2I(13, 8, 6, 6),
            Torso: new Rect2I(12, 14, 7, 7),
            BackArm: new LimbPose(new Vector2I(12, 15), new Vector2I(11, 18), new Vector2I(11, 21)),
            FrontArm: new LimbPose(new Vector2I(18, 15), new Vector2I(19, 18), new Vector2I(19, 21)),
            BackLeg: new LegPose(new Vector2I(14, 21), new Vector2I(14, 24), new Vector2I(13, 27)),
            FrontLeg: new LegPose(new Vector2I(17, 21), new Vector2I(18, 24), new Vector2I(18, 27)),
            ToolKind: DwarfToolKind.None,
            ToolStart: default,
            ToolEnd: default);

    private static DwarfFrameSpec ResolveWalkFrame(int frame)
        => frame == 0
            ? new DwarfFrameSpec(
                Head: new Rect2I(13, 9, 6, 6),
                Torso: new Rect2I(12, 15, 7, 7),
                BackArm: new LimbPose(new Vector2I(12, 16), new Vector2I(11, 18), new Vector2I(10, 20)),
                FrontArm: new LimbPose(new Vector2I(18, 16), new Vector2I(19, 18), new Vector2I(21, 20)),
                BackLeg: new LegPose(new Vector2I(14, 22), new Vector2I(13, 24), new Vector2I(12, 27)),
                FrontLeg: new LegPose(new Vector2I(17, 22), new Vector2I(19, 24), new Vector2I(20, 27)),
                ToolKind: DwarfToolKind.None,
                ToolStart: default,
                ToolEnd: default)
            : new DwarfFrameSpec(
                Head: new Rect2I(13, 8, 6, 6),
                Torso: new Rect2I(12, 14, 7, 7),
                BackArm: new LimbPose(new Vector2I(12, 15), new Vector2I(13, 17), new Vector2I(15, 20)),
                FrontArm: new LimbPose(new Vector2I(18, 15), new Vector2I(17, 18), new Vector2I(15, 20)),
                BackLeg: new LegPose(new Vector2I(14, 21), new Vector2I(15, 24), new Vector2I(16, 27)),
                FrontLeg: new LegPose(new Vector2I(17, 21), new Vector2I(16, 24), new Vector2I(14, 27)),
                ToolKind: DwarfToolKind.None,
                ToolStart: default,
                ToolEnd: default);

    private static DwarfFrameSpec ResolveSwingFrame(DwarfToolKind toolKind, int frame)
        => frame switch
        {
            0 => new DwarfFrameSpec(
                Head: new Rect2I(12, 8, 6, 6),
                Torso: new Rect2I(11, 14, 7, 7),
                BackArm: new LimbPose(new Vector2I(11, 15), new Vector2I(10, 18), new Vector2I(10, 21)),
                FrontArm: new LimbPose(new Vector2I(17, 15), new Vector2I(18, 12), new Vector2I(20, 9)),
                BackLeg: new LegPose(new Vector2I(13, 21), new Vector2I(13, 24), new Vector2I(12, 27)),
                FrontLeg: new LegPose(new Vector2I(17, 21), new Vector2I(18, 24), new Vector2I(19, 27)),
                ToolKind: toolKind,
                ToolStart: new Vector2I(20, 9),
                ToolEnd: new Vector2I(24, 6)),
            1 => new DwarfFrameSpec(
                Head: new Rect2I(14, 9, 6, 6),
                Torso: new Rect2I(13, 15, 7, 7),
                BackArm: new LimbPose(new Vector2I(13, 16), new Vector2I(12, 18), new Vector2I(12, 21)),
                FrontArm: new LimbPose(new Vector2I(19, 16), new Vector2I(21, 18), new Vector2I(23, 20)),
                BackLeg: new LegPose(new Vector2I(15, 22), new Vector2I(14, 24), new Vector2I(13, 27)),
                FrontLeg: new LegPose(new Vector2I(18, 22), new Vector2I(20, 24), new Vector2I(21, 27)),
                ToolKind: toolKind,
                ToolStart: new Vector2I(23, 20),
                ToolEnd: new Vector2I(26, 23)),
            _ => new DwarfFrameSpec(
                Head: new Rect2I(13, 8, 6, 6),
                Torso: new Rect2I(12, 14, 7, 7),
                BackArm: new LimbPose(new Vector2I(12, 15), new Vector2I(11, 18), new Vector2I(11, 20)),
                FrontArm: new LimbPose(new Vector2I(18, 15), new Vector2I(20, 14), new Vector2I(21, 16)),
                BackLeg: new LegPose(new Vector2I(14, 21), new Vector2I(14, 24), new Vector2I(13, 27)),
                FrontLeg: new LegPose(new Vector2I(17, 21), new Vector2I(18, 24), new Vector2I(18, 27)),
                ToolKind: toolKind,
                ToolStart: new Vector2I(21, 16),
                ToolEnd: new Vector2I(24, 16)),
        };

    private static DwarfFrameSpec ResolveCraftFrame(int frame)
        => frame == 0
            ? new DwarfFrameSpec(
                Head: new Rect2I(13, 9, 6, 6),
                Torso: new Rect2I(12, 15, 7, 7),
                BackArm: new LimbPose(new Vector2I(12, 16), new Vector2I(11, 18), new Vector2I(11, 20)),
                FrontArm: new LimbPose(new Vector2I(18, 16), new Vector2I(20, 15), new Vector2I(21, 14)),
                BackLeg: new LegPose(new Vector2I(14, 22), new Vector2I(14, 24), new Vector2I(13, 27)),
                FrontLeg: new LegPose(new Vector2I(17, 22), new Vector2I(18, 24), new Vector2I(19, 27)),
                ToolKind: DwarfToolKind.Hammer,
                ToolStart: new Vector2I(21, 14),
                ToolEnd: new Vector2I(24, 12))
            : new DwarfFrameSpec(
                Head: new Rect2I(14, 10, 6, 6),
                Torso: new Rect2I(13, 16, 7, 7),
                BackArm: new LimbPose(new Vector2I(13, 17), new Vector2I(12, 19), new Vector2I(12, 21)),
                FrontArm: new LimbPose(new Vector2I(19, 17), new Vector2I(21, 18), new Vector2I(22, 20)),
                BackLeg: new LegPose(new Vector2I(15, 23), new Vector2I(15, 25), new Vector2I(14, 27)),
                FrontLeg: new LegPose(new Vector2I(18, 23), new Vector2I(19, 25), new Vector2I(20, 27)),
                ToolKind: DwarfToolKind.Hammer,
                ToolStart: new Vector2I(22, 20),
                ToolEnd: new Vector2I(25, 21));

    private static DwarfFrameSpec ResolveGatherFrame(int frame)
        => frame == 0
            ? new DwarfFrameSpec(
                Head: new Rect2I(14, 10, 6, 6),
                Torso: new Rect2I(13, 16, 7, 6),
                BackArm: new LimbPose(new Vector2I(13, 17), new Vector2I(14, 19), new Vector2I(14, 22)),
                FrontArm: new LimbPose(new Vector2I(19, 17), new Vector2I(21, 19), new Vector2I(22, 22)),
                BackLeg: new LegPose(new Vector2I(15, 22), new Vector2I(14, 25), new Vector2I(14, 27)),
                FrontLeg: new LegPose(new Vector2I(18, 22), new Vector2I(19, 25), new Vector2I(19, 27)),
                ToolKind: DwarfToolKind.None,
                ToolStart: default,
                ToolEnd: default)
            : new DwarfFrameSpec(
                Head: new Rect2I(15, 11, 6, 6),
                Torso: new Rect2I(14, 17, 7, 6),
                BackArm: new LimbPose(new Vector2I(14, 18), new Vector2I(15, 20), new Vector2I(16, 23)),
                FrontArm: new LimbPose(new Vector2I(20, 18), new Vector2I(22, 19), new Vector2I(24, 20)),
                BackLeg: new LegPose(new Vector2I(16, 23), new Vector2I(15, 25), new Vector2I(15, 27)),
                FrontLeg: new LegPose(new Vector2I(19, 23), new Vector2I(20, 25), new Vector2I(20, 27)),
                ToolKind: DwarfToolKind.None,
                ToolStart: default,
                ToolEnd: default);

    private static Vector2 ResolveCarryHoldPoint(DwarfFrameSpec frame)
    {
        var backHand = new Vector2(frame.BackArm.Hand.X + 0.5f, frame.BackArm.Hand.Y + 0.5f);
        var frontHand = new Vector2(frame.FrontArm.Hand.X + 0.5f, frame.FrontArm.Hand.Y + 0.5f);
        return backHand.Lerp(frontHand, CarryHoldFrontHandBias) + CarryHoldPixelOffset;
    }

    private static void DrawTorso(Image image, Rect2I torso, DwarfPalette palette)
    {
        FillLogicalRect(image, torso, palette.Tunic);
        FillLogicalRect(image, new Rect2I(torso.Position.X + 1, torso.Position.Y + 1, torso.Size.X - 2, Math.Max(1, torso.Size.Y - 3)), palette.TunicHighlight);
        FillLogicalRect(image, new Rect2I(torso.Position.X, torso.Position.Y, torso.Size.X, 1), palette.TunicShadow);
        FillLogicalRect(image, new Rect2I(torso.Position.X + 1, torso.End.Y - 2, torso.Size.X - 2, 1), palette.Belt);
        FillLogicalRect(image, new Rect2I(torso.Position.X, torso.End.Y - 1, torso.Size.X, 1), palette.TunicShadow);
        FillLogicalRect(image, new Rect2I(torso.Position.X + torso.Size.X - 2, torso.Position.Y + 2, 1, torso.Size.Y - 3), palette.TunicShadow);
    }

    private static void DrawBackArm(Image image, LimbPose arm, DwarfPalette palette)
        => DrawArm(image, arm, palette.TunicShadow, palette.SkinShadow);

    private static void DrawFrontArm(Image image, LimbPose arm, DwarfPalette palette)
        => DrawArm(image, arm, palette.Tunic, palette.Skin);

    private static void DrawArm(Image image, LimbPose arm, Color sleeveColor, Color skinColor)
    {
        DrawLogicalLine(image, arm.Shoulder, arm.Elbow, 1, sleeveColor);
        DrawLogicalLine(image, arm.Elbow, arm.Hand, 1, sleeveColor);
        FillLogicalRect(image, new Rect2I(arm.Hand.X, arm.Hand.Y, 1, 1), skinColor);
    }

    private static void DrawBackLeg(Image image, LegPose leg, DwarfPalette palette)
        => DrawLeg(image, leg, palette.Pants.Darkened(0.10f), palette.Boots);

    private static void DrawFrontLeg(Image image, LegPose leg, DwarfPalette palette)
        => DrawLeg(image, leg, palette.Pants, palette.Boots);

    private static void DrawLeg(Image image, LegPose leg, Color pantsColor, Color bootsColor)
    {
        DrawLogicalLine(image, leg.Hip, leg.Knee, 1, pantsColor);
        DrawLogicalLine(image, leg.Knee, leg.Foot, 1, pantsColor);
        FillLogicalRect(image, new Rect2I(leg.Foot.X, leg.Foot.Y, 2, 1), bootsColor);
    }

    private static void DrawHead(Image image, Rect2I head, DwarfAppearanceComponent appearance, DwarfPalette palette)
    {
        DrawHeadBase(image, head, appearance.FaceType, palette.Skin, palette.SkinShadow, palette.SkinHighlight);
        DrawHair(image, head, appearance.HairType, palette.Hair, palette.HairShadow);
        DrawEye(image, head, appearance.EyeType);
        DrawNose(image, head, appearance.NoseType, palette.SkinShadow);
        DrawMouth(image, head, appearance.MouthType);
        DrawBeard(image, head, appearance.BeardType, palette.Beard, palette.BeardShadow);
    }

    private static void DrawHeadBase(Image image, Rect2I head, DwarfFaceType faceType, Color skin, Color skinShadow, Color skinHighlight)
    {
        switch (faceType)
        {
            case DwarfFaceType.Square:
                FillHeadRows(image, head, new[] { 0, 0, 0, 0, 0, 1 }, new[] { 5, 6, 6, 6, 6, 4 }, skin);
                break;

            case DwarfFaceType.Long:
                FillHeadRows(image, head, new[] { 1, 0, 0, 0, 1, 1 }, new[] { 4, 5, 5, 5, 4, 3 }, skin);
                FillLogicalRect(image, new Rect2I(head.Position.X + 4, head.End.Y - 1, 2, 1), skinShadow);
                break;

            case DwarfFaceType.Wide:
                FillHeadRows(image, head, new[] { 0, 0, 0, 0, 0, 1 }, new[] { 6, 6, 6, 6, 6, 4 }, skin);
                break;

            default:
                FillHeadRows(image, head, new[] { 1, 0, 0, 0, 0, 1 }, new[] { 4, 5, 6, 6, 5, 4 }, skin);
                break;
        }

        FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y + 1, 2, 1), skinHighlight);
        FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y + 3, 1, 2), skinShadow);
        FillLogicalRect(image, new Rect2I(head.End.X - 2, head.Position.Y + 4, 1, 1), skinShadow);
        FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y + 3, 1, 1), skinShadow.Lightened(0.10f));
    }

    private static void FillHeadRows(Image image, Rect2I head, int[] offsets, int[] widths, Color color)
    {
        var rowCount = Math.Min(offsets.Length, Math.Min(widths.Length, head.Size.Y));
        for (var row = 0; row < rowCount; row++)
            FillLogicalRect(image, new Rect2I(head.Position.X + offsets[row], head.Position.Y + row, widths[row], 1), color);
    }

    private static void DrawHair(Image image, Rect2I head, DwarfHairType hairType, Color hair, Color hairShadow)
    {
        switch (hairType)
        {
            case DwarfHairType.Bald:
                FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y, 2, 1), hairShadow.Lightened(0.30f));
                break;

            case DwarfHairType.Crop:
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, 5, 1), hair);
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y + 1, 2, 1), hairShadow);
                break;

            case DwarfHairType.Swept:
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, 5, 1), hair);
                FillLogicalRect(image, new Rect2I(head.Position.X + 2, head.Position.Y + 1, 3, 1), hair);
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y + 2, 1, 2), hairShadow);
                break;

            case DwarfHairType.Shaggy:
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, 5, 1), hair);
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y + 1, 2, 3), hairShadow);
                FillLogicalRect(image, new Rect2I(head.End.X - 2, head.Position.Y + 1, 2, 2), hair);
                break;

            case DwarfHairType.Crest:
                DrawLogicalLine(image, new Vector2I(head.Position.X + 2, head.Position.Y - 1), new Vector2I(head.Position.X + 3, head.Position.Y + 2), 1, hair);
                break;

            case DwarfHairType.Braided:
                FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, 5, 1), hair);
                DrawLogicalLine(image, new Vector2I(head.Position.X, head.Position.Y + 2), new Vector2I(head.Position.X - 1, head.End.Y + 2), 1, hairShadow);
                break;
        }
    }

    private static void DrawEye(Image image, Rect2I head, DwarfEyeType eyeType)
    {
        var eyeX = head.End.X - 2;
        var eyeY = head.Position.Y + 2;
        var pupil = new Color(0.05f, 0.05f, 0.07f);

        switch (eyeType)
        {
            case DwarfEyeType.Narrow:
                FillLogicalRect(image, new Rect2I(eyeX - 1, eyeY, 2, 1), pupil);
                break;

            case DwarfEyeType.Wide:
                FillLogicalRect(image, new Rect2I(eyeX, eyeY, 1, 2), pupil);
                break;

            case DwarfEyeType.HeavyBrow:
                FillLogicalRect(image, new Rect2I(eyeX - 1, eyeY - 1, 2, 1), pupil);
                FillLogicalRect(image, new Rect2I(eyeX, eyeY, 1, 1), pupil);
                break;

            case DwarfEyeType.Bright:
                FillLogicalRect(image, new Rect2I(eyeX - 1, eyeY, 2, 1), new Color(0.98f, 0.98f, 1f));
                FillLogicalRect(image, new Rect2I(eyeX, eyeY, 1, 1), pupil);
                break;

            default:
                FillLogicalRect(image, new Rect2I(eyeX, eyeY, 1, 1), pupil);
                break;
        }
    }

    private static void DrawNose(Image image, Rect2I head, DwarfNoseType noseType, Color noseColor)
    {
        var baseX = head.End.X - 1;
        var baseY = head.Position.Y + 3;
        switch (noseType)
        {
            case DwarfNoseType.Broad:
                FillLogicalRect(image, new Rect2I(baseX, baseY, 2, 1), noseColor);
                break;

            case DwarfNoseType.Long:
                FillLogicalRect(image, new Rect2I(baseX, baseY - 1, 1, 2), noseColor);
                break;

            case DwarfNoseType.Hooked:
                FillLogicalRect(image, new Rect2I(baseX, baseY - 1, 1, 2), noseColor);
                FillLogicalRect(image, new Rect2I(baseX, baseY + 1, 2, 1), noseColor);
                break;

            default:
                FillLogicalRect(image, new Rect2I(baseX, baseY, 1, 1), noseColor);
                break;
        }
    }

    private static void DrawMouth(Image image, Rect2I head, DwarfMouthType mouthType)
    {
        var lip = new Color(0.30f, 0.12f, 0.12f);
        var x = head.End.X - 3;
        var y = head.End.Y - 1;
        switch (mouthType)
        {
            case DwarfMouthType.Smile:
                FillLogicalRect(image, new Rect2I(x, y, 2, 1), lip);
                FillLogicalRect(image, new Rect2I(x + 2, y - 1, 1, 1), lip);
                break;

            case DwarfMouthType.Smirk:
                FillLogicalRect(image, new Rect2I(x + 1, y - 1, 2, 1), lip);
                break;

            case DwarfMouthType.Frown:
                FillLogicalRect(image, new Rect2I(x, y - 1, 2, 1), lip);
                FillLogicalRect(image, new Rect2I(x + 2, y, 1, 1), lip);
                break;

            case DwarfMouthType.Open:
                FillLogicalRect(image, new Rect2I(x + 1, y - 1, 1, 2), lip);
                break;

            default:
                FillLogicalRect(image, new Rect2I(x, y, 2, 1), lip);
                break;
        }
    }

    private static void DrawBeard(Image image, Rect2I head, DwarfBeardType beardType, Color beard, Color beardShadow)
    {
        switch (beardType)
        {
            case DwarfBeardType.Clean:
                return;

            case DwarfBeardType.Short:
                FillLogicalRect(image, new Rect2I(head.Position.X + 3, head.End.Y - 1, 2, 2), beard);
                break;

            case DwarfBeardType.Full:
                FillLogicalRect(image, new Rect2I(head.Position.X + 2, head.End.Y - 1, 3, 4), beard);
                FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.End.Y + 1, 2, 2), beardShadow);
                break;

            case DwarfBeardType.Braided:
                FillLogicalRect(image, new Rect2I(head.Position.X + 3, head.End.Y - 1, 2, 2), beard);
                DrawLogicalLine(image, new Vector2I(head.Position.X + 4, head.End.Y + 1), new Vector2I(head.Position.X + 5, head.End.Y + 4), 1, beardShadow);
                break;

            case DwarfBeardType.Forked:
                FillLogicalRect(image, new Rect2I(head.Position.X + 2, head.End.Y - 1, 3, 2), beard);
                DrawLogicalLine(image, new Vector2I(head.Position.X + 3, head.End.Y + 1), new Vector2I(head.Position.X + 2, head.End.Y + 4), 1, beardShadow);
                DrawLogicalLine(image, new Vector2I(head.Position.X + 4, head.End.Y + 1), new Vector2I(head.Position.X + 5, head.End.Y + 4), 1, beardShadow);
                break;

            case DwarfBeardType.Mutton:
                FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y + 3, 2, 2), beardShadow);
                FillLogicalRect(image, new Rect2I(head.Position.X + 3, head.End.Y - 1, 2, 2), beard);
                break;
        }
    }

    private static void DrawTool(Image image, DwarfToolKind toolKind, Vector2I start, Vector2I end, DwarfPalette palette)
    {
        if (toolKind == DwarfToolKind.None)
            return;

        var handleColor = new Color(0.56f, 0.37f, 0.18f);
        DrawLogicalLine(image, start, end, 1, handleColor);

        switch (toolKind)
        {
            case DwarfToolKind.Pickaxe:
                DrawLogicalLine(image, new Vector2I(end.X - 1, end.Y - 1), new Vector2I(end.X + 1, end.Y + 1), 1, palette.Metal);
                DrawLogicalLine(image, new Vector2I(end.X - 1, end.Y + 1), new Vector2I(end.X + 1, end.Y - 1), 1, palette.Metal);
                break;

            case DwarfToolKind.Axe:
                FillLogicalRect(image, new Rect2I(end.X, end.Y - 1, 2, 2), palette.Metal);
                FillLogicalRect(image, new Rect2I(end.X - 1, end.Y, 1, 2), palette.Metal.Lightened(0.08f));
                break;

            case DwarfToolKind.Hammer:
                FillLogicalRect(image, new Rect2I(end.X - 1, end.Y - 1, 3, 2), palette.Metal);
                break;

            case DwarfToolKind.Sword:
                DrawLogicalLine(image, end, new Vector2I(end.X + 2, end.Y), 1, palette.Metal);
                FillLogicalRect(image, new Rect2I(end.X - 1, end.Y - 1, 2, 1), palette.Belt);
                break;
        }
    }

    private static Color ResolveHairColor(DwarfHairColor color)
        => color switch
        {
            DwarfHairColor.Coal => new Color(0.12f, 0.10f, 0.10f),
            DwarfHairColor.Chestnut => new Color(0.40f, 0.20f, 0.10f),
            DwarfHairColor.Copper => new Color(0.69f, 0.34f, 0.14f),
            DwarfHairColor.Blond => new Color(0.82f, 0.72f, 0.40f),
            DwarfHairColor.Ash => new Color(0.42f, 0.40f, 0.38f),
            DwarfHairColor.Silver => new Color(0.72f, 0.72f, 0.76f),
            _ => new Color(0.12f, 0.10f, 0.10f),
        };

    private static Image NewImage()
    {
        var image = Image.CreateEmpty(CanvasSize, CanvasSize, false, Image.Format.Rgba8);
        image.Fill(new Color(0f, 0f, 0f, 0f));
        return image;
    }

    private static void FillLogicalRect(Image image, Rect2I rect, Color color)
    {
        var scaledRect = new Rect2I(rect.Position.X * PixelScale, rect.Position.Y * PixelScale, rect.Size.X * PixelScale, rect.Size.Y * PixelScale);
        image.FillRect(scaledRect, color);
    }

    private static void DrawLogicalLine(Image image, Vector2I start, Vector2I end, int logicalThickness, Color color)
    {
        var currentX = start.X;
        var currentY = start.Y;
        var deltaX = Math.Abs(end.X - start.X);
        var deltaY = Math.Abs(end.Y - start.Y);
        var stepX = start.X < end.X ? 1 : -1;
        var stepY = start.Y < end.Y ? 1 : -1;
        var error = deltaX - deltaY;

        while (true)
        {
            FillLogicalRect(image, new Rect2I(currentX, currentY, logicalThickness, logicalThickness), color);
            if (currentX == end.X && currentY == end.Y)
                break;

            var errorTimesTwo = error * 2;
            if (errorTimesTwo > -deltaY)
            {
                error -= deltaY;
                currentX += stepX;
            }

            if (errorTimesTwo < deltaX)
            {
                error += deltaX;
                currentY += stepY;
            }
        }
    }

    private static void OutlineOpaqueSilhouette(Image image, Color color, int thickness)
    {
        var clampedThickness = Math.Max(1, thickness);
        for (var pass = 0; pass < clampedThickness; pass++)
        {
            var width = image.GetWidth();
            var height = image.GetHeight();
            var opaqueMask = new bool[width, height];

            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                opaqueMask[x, y] = image.GetPixel(x, y).A > 0.01f;

            for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
            {
                if (opaqueMask[x, y] || !TouchesOpaqueNeighbor(opaqueMask, width, height, x, y))
                    continue;

                image.SetPixel(x, y, color);
            }
        }
    }

    private static bool TouchesOpaqueNeighbor(bool[,] opaqueMask, int width, int height, int x, int y)
    {
        for (var deltaX = -1; deltaX <= 1; deltaX++)
        for (var deltaY = -1; deltaY <= 1; deltaY++)
        {
            if (deltaX == 0 && deltaY == 0)
                continue;

            var sampleX = x + deltaX;
            var sampleY = y + deltaY;
            if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                continue;

            if (opaqueMask[sampleX, sampleY])
                return true;
        }

        return false;
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            for (var index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= 16777619;
            }

            return hash;
        }
    }
}