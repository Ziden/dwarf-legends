using Godot;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        GetNode<Button>("%PlayButton").Pressed +=
            () => GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");

        GetNode<Button>("%WorldGenButton").Pressed +=
            () => GetTree().ChangeSceneToFile("res://Scenes/WorldGenViewer.tscn");
    }
}
