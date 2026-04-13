using System;
using DwarfFortress.WorldGen.Content;
using Godot;

namespace DwarfFortress.GodotClient.Rendering;

internal enum CreatureSpriteFacing
{
    Left,
    Right,
}

internal enum CreatureSpriteActionKind
{
    Idle,
    Walk,
}

internal readonly record struct CreatureSpritePose(CreatureSpriteFacing Facing, CreatureSpriteActionKind Action, int Frame)
{
    public static CreatureSpritePose Idle(CreatureSpriteFacing facing = CreatureSpriteFacing.Right)
        => new(facing, CreatureSpriteActionKind.Idle, 0);
}

internal static class CreatureSpriteComposer
{
    private const int CanvasSize = PixelArtFactory.Size;
    private const int PixelScale = 2;
    private const int OutlineThickness = 1;

    private enum CreatureBodyPlan
    {
        Biped,
        Quadruped,
        Swimmer,
    }

    private enum CreatureProfileFlavor
    {
        Goblin,
        Troll,
        Cat,
        Dog,
        Elk,
        GiantCarp,
        FallbackBiped,
        FallbackQuadruped,
        FallbackSwimmer,
    }

    private readonly record struct CreaturePalette(
        Color Body,
        Color BodyShadow,
        Color BodyHighlight,
        Color Detail,
        Color DetailShadow,
        Color Eye,
        Color Outline);

    private readonly record struct CreatureSpec(
        CreatureProfileFlavor Flavor,
        CreatureBodyPlan BodyPlan,
        CreaturePalette Palette);

    public static int GetFrameCount(CreatureSpriteActionKind action)
        => action == CreatureSpriteActionKind.Walk ? 2 : 1;

    public static Texture2D Create(string defId, string? profileId, string? movementModeId, Color? viewerColor, CreatureSpritePose pose)
    {
        var normalizedPose = NormalizePose(pose);
        var spec = ResolveSpec(defId, profileId, movementModeId, viewerColor);
        var image = NewImage();

        switch (spec.BodyPlan)
        {
            case CreatureBodyPlan.Biped:
                DrawBiped(image, spec, normalizedPose);
                break;

            case CreatureBodyPlan.Swimmer:
                DrawSwimmer(image, spec, normalizedPose);
                break;

            default:
                DrawQuadruped(image, spec, normalizedPose);
                break;
        }

        if (normalizedPose.Facing == CreatureSpriteFacing.Left)
            image.FlipX();

        OutlineOpaqueSilhouette(image, spec.Palette.Outline, OutlineThickness);
        return ImageTexture.CreateFromImage(image);
    }

    private static CreatureSpritePose NormalizePose(CreatureSpritePose pose)
    {
        var frameCount = GetFrameCount(pose.Action);
        if (frameCount <= 1)
            return new CreatureSpritePose(pose.Facing, pose.Action, 0);

        var normalizedFrame = ((pose.Frame % frameCount) + frameCount) % frameCount;
        return new CreatureSpritePose(pose.Facing, pose.Action, normalizedFrame);
    }

    private static CreatureSpec ResolveSpec(string defId, string? profileId, string? movementModeId, Color? viewerColor)
    {
        return profileId switch
        {
            ContentCreatureVisualProfileIds.Goblin => new CreatureSpec(
                CreatureProfileFlavor.Goblin,
                CreatureBodyPlan.Biped,
                new CreaturePalette(
                    Body: new Color(0.27f, 0.35f, 0.19f),
                    BodyShadow: new Color(0.18f, 0.24f, 0.12f),
                    BodyHighlight: new Color(0.35f, 0.44f, 0.24f),
                    Detail: new Color(0.47f, 0.78f, 0.33f),
                    DetailShadow: new Color(0.33f, 0.58f, 0.23f),
                    Eye: new Color(0.83f, 0.15f, 0.12f),
                    Outline: new Color(0.13f, 0.21f, 0.11f, 0.96f))),
            ContentCreatureVisualProfileIds.Troll => new CreatureSpec(
                CreatureProfileFlavor.Troll,
                CreatureBodyPlan.Biped,
                new CreaturePalette(
                    Body: new Color(0.56f, 0.64f, 0.55f),
                    BodyShadow: new Color(0.40f, 0.48f, 0.40f),
                    BodyHighlight: new Color(0.67f, 0.73f, 0.65f),
                    Detail: new Color(0.66f, 0.71f, 0.64f),
                    DetailShadow: new Color(0.46f, 0.53f, 0.46f),
                    Eye: new Color(0.16f, 0.17f, 0.16f),
                    Outline: new Color(0.20f, 0.27f, 0.19f, 0.96f))),
            ContentCreatureVisualProfileIds.Cat => new CreatureSpec(
                CreatureProfileFlavor.Cat,
                CreatureBodyPlan.Quadruped,
                new CreaturePalette(
                    Body: new Color(0.80f, 0.62f, 0.27f),
                    BodyShadow: new Color(0.61f, 0.45f, 0.16f),
                    BodyHighlight: new Color(0.90f, 0.74f, 0.40f),
                    Detail: new Color(0.94f, 0.87f, 0.70f),
                    DetailShadow: new Color(0.68f, 0.50f, 0.22f),
                    Eye: new Color(0.14f, 0.20f, 0.07f),
                    Outline: new Color(0.29f, 0.19f, 0.10f, 0.96f))),
            ContentCreatureVisualProfileIds.Dog => new CreatureSpec(
                CreatureProfileFlavor.Dog,
                CreatureBodyPlan.Quadruped,
                new CreaturePalette(
                    Body: new Color(0.49f, 0.35f, 0.21f),
                    BodyShadow: new Color(0.32f, 0.22f, 0.13f),
                    BodyHighlight: new Color(0.60f, 0.44f, 0.28f),
                    Detail: new Color(0.83f, 0.75f, 0.62f),
                    DetailShadow: new Color(0.58f, 0.44f, 0.30f),
                    Eye: new Color(0.09f, 0.07f, 0.05f),
                    Outline: new Color(0.22f, 0.15f, 0.09f, 0.96f))),
            ContentCreatureVisualProfileIds.Elk => new CreatureSpec(
                CreatureProfileFlavor.Elk,
                CreatureBodyPlan.Quadruped,
                new CreaturePalette(
                    Body: new Color(0.58f, 0.38f, 0.21f),
                    BodyShadow: new Color(0.39f, 0.24f, 0.12f),
                    BodyHighlight: new Color(0.69f, 0.48f, 0.29f),
                    Detail: new Color(0.86f, 0.76f, 0.60f),
                    DetailShadow: new Color(0.64f, 0.54f, 0.39f),
                    Eye: new Color(0.10f, 0.07f, 0.05f),
                    Outline: new Color(0.27f, 0.17f, 0.09f, 0.96f))),
            ContentCreatureVisualProfileIds.GiantCarp => new CreatureSpec(
                CreatureProfileFlavor.GiantCarp,
                CreatureBodyPlan.Swimmer,
                new CreaturePalette(
                    Body: new Color(0.25f, 0.58f, 0.78f),
                    BodyShadow: new Color(0.16f, 0.41f, 0.58f),
                    BodyHighlight: new Color(0.44f, 0.75f, 0.90f),
                    Detail: new Color(0.18f, 0.47f, 0.68f),
                    DetailShadow: new Color(0.12f, 0.32f, 0.47f),
                    Eye: new Color(0.96f, 0.96f, 0.98f),
                    Outline: new Color(0.12f, 0.28f, 0.42f, 0.96f))),
            _ => ResolveFallbackSpec(defId, movementModeId, viewerColor),
        };
    }

    private static CreatureSpec ResolveFallbackSpec(string defId, string? movementModeId, Color? viewerColor)
    {
        var baseColor = viewerColor ?? ResolveHashedBaseColor(defId);
        var palette = new CreaturePalette(
            Body: baseColor,
            BodyShadow: baseColor.Darkened(0.20f),
            BodyHighlight: baseColor.Lightened(0.14f),
            Detail: baseColor.Lightened(0.26f),
            DetailShadow: baseColor.Darkened(0.10f),
            Eye: new Color(0.08f, 0.07f, 0.06f),
            Outline: new Color(baseColor.Darkened(0.48f).R, baseColor.Darkened(0.48f).G, baseColor.Darkened(0.48f).B, 0.96f));

        var bodyPlan = ResolveFallbackBodyPlan(defId, movementModeId);
        var flavor = bodyPlan switch
        {
            CreatureBodyPlan.Biped => CreatureProfileFlavor.FallbackBiped,
            CreatureBodyPlan.Swimmer => CreatureProfileFlavor.FallbackSwimmer,
            _ => CreatureProfileFlavor.FallbackQuadruped,
        };

        return new CreatureSpec(flavor, bodyPlan, palette);
    }

    private static CreatureBodyPlan ResolveFallbackBodyPlan(string defId, string? movementModeId)
    {
        if (string.Equals(movementModeId, ContentCreatureMovementModeIds.Aquatic, StringComparison.OrdinalIgnoreCase)
            || string.Equals(movementModeId, ContentCreatureMovementModeIds.Swimmer, StringComparison.OrdinalIgnoreCase))
        {
            return CreatureBodyPlan.Swimmer;
        }

        return (StableHash(defId) & 1) == 0
            ? CreatureBodyPlan.Quadruped
            : CreatureBodyPlan.Biped;
    }

    private static void DrawQuadruped(Image image, CreatureSpec spec, CreatureSpritePose pose)
    {
        var frame = pose.Action == CreatureSpriteActionKind.Walk ? pose.Frame : 0;
        var bob = pose.Action == CreatureSpriteActionKind.Walk && frame == 1 ? 1 : 0;

        Rect2I body;
        Rect2I head;
        Vector2I tailBase;
        Vector2I tailTip;
        var legLength = 6;
        var frontLegRoots = new[] { new Vector2I(0, 0), new Vector2I(0, 0) };
        var backLegRoots = new[] { new Vector2I(0, 0), new Vector2I(0, 0) };
        var pointedEars = false;
        var floppyEars = false;
        var antlers = false;
        var muzzle = false;

        switch (spec.Flavor)
        {
            case CreatureProfileFlavor.Cat:
                body = new Rect2I(10, 16 + bob, 10, 4);
                head = new Rect2I(20, 14 + bob, 5, 4);
                tailBase = new Vector2I(body.Position.X, body.Position.Y + 1);
                tailTip = frame == 0 ? new Vector2I(body.Position.X - 4, body.Position.Y - 2) : new Vector2I(body.Position.X - 4, body.Position.Y + 2);
                legLength = 5;
                backLegRoots = [new Vector2I(12, body.End.Y - 1), new Vector2I(15, body.End.Y - 1)];
                frontLegRoots = [new Vector2I(18, body.End.Y - 1), new Vector2I(20, body.End.Y - 1)];
                pointedEars = true;
                break;

            case CreatureProfileFlavor.Dog:
                body = new Rect2I(9, 15 + bob, 13, 5);
                head = new Rect2I(22, 13 + bob, 6, 5);
                tailBase = new Vector2I(body.Position.X, body.Position.Y + 1);
                tailTip = frame == 0 ? new Vector2I(body.Position.X - 4, body.Position.Y - 1) : new Vector2I(body.Position.X - 4, body.Position.Y + 2);
                backLegRoots = [new Vector2I(11, body.End.Y - 1), new Vector2I(15, body.End.Y - 1)];
                frontLegRoots = [new Vector2I(19, body.End.Y - 1), new Vector2I(22, body.End.Y - 1)];
                floppyEars = true;
                muzzle = true;
                break;

            case CreatureProfileFlavor.Elk:
                body = new Rect2I(7, 13 + bob, 16, 6);
                head = new Rect2I(23, 11 + bob, 6, 6);
                tailBase = new Vector2I(body.Position.X, body.Position.Y + 1);
                tailTip = new Vector2I(body.Position.X - 3, body.Position.Y + (frame == 0 ? 1 : 2));
                legLength = 9;
                backLegRoots = [new Vector2I(10, body.End.Y - 1), new Vector2I(15, body.End.Y - 1)];
                frontLegRoots = [new Vector2I(20, body.End.Y - 1), new Vector2I(24, body.End.Y - 1)];
                antlers = true;
                muzzle = true;
                break;

            default:
                body = new Rect2I(9, 15 + bob, 12, 5);
                head = new Rect2I(21, 13 + bob, 5, 5);
                tailBase = new Vector2I(body.Position.X, body.Position.Y + 1);
                tailTip = new Vector2I(body.Position.X - 4, body.Position.Y + (frame == 0 ? 0 : 2));
                backLegRoots = [new Vector2I(11, body.End.Y - 1), new Vector2I(14, body.End.Y - 1)];
                frontLegRoots = [new Vector2I(18, body.End.Y - 1), new Vector2I(21, body.End.Y - 1)];
                pointedEars = true;
                break;
        }

        var stride = frame == 0 ? -1 : 1;
        DrawQuadrupedLeg(image, backLegRoots[0], new Vector2I(backLegRoots[0].X + stride, backLegRoots[0].Y + 3), new Vector2I(backLegRoots[0].X + stride, backLegRoots[0].Y + legLength), spec.Palette.BodyShadow, spec.Palette.DetailShadow);
        DrawQuadrupedLeg(image, backLegRoots[1], new Vector2I(backLegRoots[1].X - stride, backLegRoots[1].Y + 3), new Vector2I(backLegRoots[1].X - stride, backLegRoots[1].Y + legLength - 1), spec.Palette.BodyShadow, spec.Palette.DetailShadow);
        DrawLogicalLine(image, tailBase, tailTip, 1, spec.Palette.BodyShadow);

        DrawQuadrupedBody(image, body, head, spec.Palette);

        DrawQuadrupedLeg(image, frontLegRoots[0], new Vector2I(frontLegRoots[0].X - stride, frontLegRoots[0].Y + 3), new Vector2I(frontLegRoots[0].X - stride, frontLegRoots[0].Y + legLength), spec.Palette.Body, spec.Palette.DetailShadow);
        DrawQuadrupedLeg(image, frontLegRoots[1], new Vector2I(frontLegRoots[1].X + stride, frontLegRoots[1].Y + 3), new Vector2I(frontLegRoots[1].X + stride, frontLegRoots[1].Y + legLength - 1), spec.Palette.Body, spec.Palette.DetailShadow);

        if (muzzle)
            FillLogicalRect(image, new Rect2I(head.End.X - 2, head.Position.Y + 2, 2, 2), spec.Palette.Detail);

        if (pointedEars)
        {
            FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y - 1, 1, 1), spec.Palette.DetailShadow);
            FillLogicalRect(image, new Rect2I(head.Position.X + 3, head.Position.Y - 1, 1, 1), spec.Palette.DetailShadow);
        }

        if (floppyEars)
        {
            DrawLogicalLine(image, new Vector2I(head.Position.X + 1, head.Position.Y + 1), new Vector2I(head.Position.X, head.Position.Y + 4), 1, spec.Palette.DetailShadow);
            DrawLogicalLine(image, new Vector2I(head.Position.X + 3, head.Position.Y + 1), new Vector2I(head.Position.X + 2, head.Position.Y + 4), 1, spec.Palette.DetailShadow);
        }

        if (antlers)
        {
            DrawLogicalLine(image, new Vector2I(head.Position.X + 1, head.Position.Y), new Vector2I(head.Position.X, head.Position.Y - 4), 1, spec.Palette.Detail);
            DrawLogicalLine(image, new Vector2I(head.Position.X + 3, head.Position.Y), new Vector2I(head.Position.X + 2, head.Position.Y - 5), 1, spec.Palette.Detail);
            FillLogicalRect(image, new Rect2I(head.Position.X - 1, head.Position.Y - 4, 3, 1), spec.Palette.Detail);
            FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y - 5, 3, 1), spec.Palette.Detail);
        }

        FillLogicalRect(image, new Rect2I(head.End.X - 2, head.Position.Y + 1, 1, 1), spec.Palette.Eye);
    }

    private static void DrawQuadrupedBody(Image image, Rect2I body, Rect2I head, CreaturePalette palette)
    {
        FillLogicalRect(image, body, palette.Body);
        FillLogicalRect(image, new Rect2I(body.Position.X + 1, body.Position.Y + 1, Math.Max(1, body.Size.X - 2), Math.Max(1, body.Size.Y - 2)), palette.BodyHighlight);
        FillLogicalRect(image, new Rect2I(body.Position.X, body.Position.Y, body.Size.X, 1), palette.BodyShadow);
        FillLogicalRect(image, new Rect2I(body.Position.X + 1, body.End.Y - 1, Math.Max(1, body.Size.X - 2), 1), palette.BodyShadow);
        DrawLogicalLine(image, new Vector2I(body.End.X - 1, body.Position.Y + 2), new Vector2I(head.Position.X, head.Position.Y + 2), 1, palette.BodyShadow);
        FillLogicalRect(image, head, palette.Body);
        FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y + 1, Math.Max(1, head.Size.X - 2), Math.Max(1, head.Size.Y - 2)), palette.BodyHighlight);
        FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, head.Size.X, 1), palette.BodyShadow);
    }

    private static void DrawQuadrupedLeg(Image image, Vector2I hip, Vector2I knee, Vector2I foot, Color legColor, Color hoofColor)
    {
        DrawLogicalLine(image, hip, knee, 1, legColor);
        DrawLogicalLine(image, knee, foot, 1, legColor);
        FillLogicalRect(image, new Rect2I(foot.X, foot.Y, 2, 1), hoofColor);
    }

    private static void DrawBiped(Image image, CreatureSpec spec, CreatureSpritePose pose)
    {
        var frame = pose.Action == CreatureSpriteActionKind.Walk ? pose.Frame : 0;
        var bob = pose.Action == CreatureSpriteActionKind.Walk && frame == 1 ? 1 : 0;

        Rect2I torso;
        Rect2I head;
        var pointedEar = false;
        var tusks = false;

        switch (spec.Flavor)
        {
            case CreatureProfileFlavor.Troll:
                torso = new Rect2I(10, 14 + bob, 9, 8);
                head = new Rect2I(18, 9 + bob, 7, 7);
                tusks = true;
                break;

            case CreatureProfileFlavor.Goblin:
                torso = new Rect2I(11, 15 + bob, 7, 7);
                head = new Rect2I(18, 10 + bob, 6, 6);
                pointedEar = true;
                break;

            default:
                torso = new Rect2I(11, 15 + bob, 7, 7);
                head = new Rect2I(18, 10 + bob, 6, 6);
                break;
        }

        var stride = frame == 0 ? -1 : 1;
        var backShoulder = new Vector2I(torso.Position.X + 1, torso.Position.Y + 1);
        var frontShoulder = new Vector2I(torso.End.X - 1, torso.Position.Y + 1);
        var backHip = new Vector2I(torso.Position.X + 2, torso.End.Y - 1);
        var frontHip = new Vector2I(torso.End.X - 2, torso.End.Y - 1);

        DrawBipedLimb(image, backShoulder, new Vector2I(backShoulder.X - stride, backShoulder.Y + 3), new Vector2I(backShoulder.X - stride, backShoulder.Y + 6), spec.Palette.BodyShadow);
        DrawBipedLimb(image, backHip, new Vector2I(backHip.X + stride, backHip.Y + 3), new Vector2I(backHip.X + stride, backHip.Y + 6), spec.Palette.BodyShadow);

        FillLogicalRect(image, torso, spec.Palette.Body);
        FillLogicalRect(image, new Rect2I(torso.Position.X + 1, torso.Position.Y + 1, Math.Max(1, torso.Size.X - 2), Math.Max(1, torso.Size.Y - 2)), spec.Palette.BodyHighlight);
        FillLogicalRect(image, new Rect2I(torso.Position.X, torso.Position.Y, torso.Size.X, 1), spec.Palette.BodyShadow);
        FillLogicalRect(image, head, spec.Palette.Detail);
        FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y + 1, Math.Max(1, head.Size.X - 2), Math.Max(1, head.Size.Y - 2)), spec.Palette.Detail.Lightened(0.08f));
        FillLogicalRect(image, new Rect2I(head.Position.X, head.Position.Y, head.Size.X, 1), spec.Palette.DetailShadow);

        if (pointedEar)
        {
            FillLogicalRect(image, new Rect2I(head.Position.X + 1, head.Position.Y - 1, 1, 1), spec.Palette.DetailShadow);
            FillLogicalRect(image, new Rect2I(head.Position.X + 3, head.Position.Y - 1, 1, 1), spec.Palette.DetailShadow);
        }

        if (tusks)
        {
            FillLogicalRect(image, new Rect2I(head.End.X - 1, head.End.Y - 1, 1, 1), spec.Palette.BodyHighlight);
            FillLogicalRect(image, new Rect2I(head.End.X - 2, head.End.Y, 1, 1), spec.Palette.BodyHighlight);
        }

        FillLogicalRect(image, new Rect2I(head.End.X - 2, head.Position.Y + 2, 1, 1), spec.Palette.Eye);
        DrawBipedLimb(image, frontShoulder, new Vector2I(frontShoulder.X + stride, frontShoulder.Y + 3), new Vector2I(frontShoulder.X + stride, frontShoulder.Y + 6), spec.Palette.Body);
        DrawBipedLimb(image, frontHip, new Vector2I(frontHip.X - stride, frontHip.Y + 3), new Vector2I(frontHip.X - stride, frontHip.Y + 6), spec.Palette.Body);
    }

    private static void DrawBipedLimb(Image image, Vector2I start, Vector2I joint, Vector2I end, Color color)
    {
        DrawLogicalLine(image, start, joint, 1, color);
        DrawLogicalLine(image, joint, end, 1, color);
    }

    private static void DrawSwimmer(Image image, CreatureSpec spec, CreatureSpritePose pose)
    {
        var frame = pose.Action == CreatureSpriteActionKind.Walk ? pose.Frame : 0;
        var topY = spec.Flavor == CreatureProfileFlavor.GiantCarp ? 14 : 15;
        topY += frame == 0 ? 0 : 1;

        var offsets = spec.Flavor == CreatureProfileFlavor.GiantCarp
            ? new[] { 8, 4, 2, 0, 2, 4, 8 }
            : new[] { 7, 4, 2, 0, 2, 4, 7 };
        var widths = spec.Flavor == CreatureProfileFlavor.GiantCarp
            ? new[] { 8, 14, 18, 20, 18, 14, 8 }
            : new[] { 7, 12, 16, 18, 16, 12, 7 };

        FillLogicalRows(image, 4, topY, offsets, widths, spec.Palette.Body);
        FillLogicalRows(image, 5, topY + 1, new[] { 7, 4, 2, 1, 2, 4, 7 }, new[] { 6, 12, 15, 17, 15, 12, 6 }, spec.Palette.BodyHighlight);
        FillLogicalRect(image, new Rect2I(12, topY + 1, 10, 1), spec.Palette.BodyShadow);

        var tailRoot = new Vector2I(6, topY + 3);
        var tailTip = frame == 0
            ? new Vector2I(1, topY + 1)
            : new Vector2I(1, topY + 6);
        DrawLogicalLine(image, tailRoot, tailTip, 1, spec.Palette.Detail);
        DrawLogicalLine(image, tailRoot + new Vector2I(0, 1), tailTip + new Vector2I(0, 2), 1, spec.Palette.DetailShadow);

        var dorsalY = frame == 0 ? topY - 2 : topY - 1;
        DrawLogicalLine(image, new Vector2I(15, topY), new Vector2I(18, dorsalY), 1, spec.Palette.Detail);
        DrawLogicalLine(image, new Vector2I(17, topY + 6), new Vector2I(19, topY + 9), 1, spec.Palette.Detail);
        DrawLogicalLine(image, new Vector2I(22, topY + 3), new Vector2I(26, topY + (frame == 0 ? 2 : 4)), 1, spec.Palette.DetailShadow);

        FillLogicalRect(image, new Rect2I(22, topY + 2, 1, 1), spec.Palette.Eye);
        FillLogicalRect(image, new Rect2I(23, topY + 2, 1, 1), spec.Palette.DetailShadow);
        FillLogicalRect(image, new Rect2I(10, topY + 2, 2, 2), spec.Palette.BodyHighlight.Lightened(0.06f));
        FillLogicalRect(image, new Rect2I(15, topY + 3, 2, 2), spec.Palette.BodyHighlight.Lightened(0.04f));
    }

    private static Color ResolveHashedBaseColor(string defId)
    {
        var hash = StableHash(defId);
        return new Color(
            HashChannel(hash, 0, 0.30f, 0.84f),
            HashChannel(hash, 8, 0.26f, 0.82f),
            HashChannel(hash, 16, 0.20f, 0.78f));
    }

    private static float HashChannel(int hash, int shift, float min, float max)
    {
        var value = (hash >> shift) & 0xFF;
        var t = value / 255f;
        return min + ((max - min) * t);
    }

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

    private static void FillLogicalRows(Image image, int startX, int startY, int[] offsets, int[] widths, Color color)
    {
        var rowCount = Math.Min(offsets.Length, widths.Length);
        for (var row = 0; row < rowCount; row++)
        {
            if (widths[row] <= 0)
                continue;

            FillLogicalRect(image, new Rect2I(startX + offsets[row], startY + row, widths[row], 1), color);
        }
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