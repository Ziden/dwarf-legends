$path = '.\src\DwarfFortress.GodotClient\Scripts\GameRoot.cs'
$content = Get-Content -Path $path -Raw

$old1 = @'
    private WorldRender3DPlaceholder? _world3DPlaceholder;
'@
$new1 = @'
    private WorldRender3DPlaceholder? _world3DPlaceholder;
    private WorldSelectionRing3D? _worldSelectionRing3D;
'@
if (-not $content.Contains($old1))
{
    throw 'Field anchor not found.'
}
$content = $content.Replace($old1, $new1)

$old2 = @'
        _camera3D        = GetNodeOrNull<Camera3D>("%MainCamera3D");
        _world3DPlaceholder = GetNodeOrNull<WorldRender3DPlaceholder>("%World3DRoot");
        _viewport.Initialize(_camera, 0);
'@
$new2 = @'
        _camera3D        = GetNodeOrNull<Camera3D>("%MainCamera3D");
        _world3DPlaceholder = GetNodeOrNull<WorldRender3DPlaceholder>("%World3DRoot");
        if (_world3DPlaceholder is not null)
        {
            _worldSelectionRing3D = new WorldSelectionRing3D();
            _world3DPlaceholder.AddChild(_worldSelectionRing3D);
        }
        _viewport.Initialize(_camera, 0);
'@
if (-not $content.Contains($old2))
{
    throw 'Ready anchor not found.'
}
$content = $content.Replace($old2, $new2)

$old3 = @'
            _renderCache.Invalidate();
            _world3DPlaceholder?.Reset();
            _hasWorld3DVisibleState = false;
'@
$new3 = @'
            _renderCache.Invalidate();
            _world3DPlaceholder?.Reset();
            _worldSelectionRing3D?.SetActive(!UsesCanvasWorldRenderer);
            _hasWorld3DVisibleState = false;
'@
if (-not $content.Contains($old3))
{
    throw 'Startup reset anchor not found.'
}
$content = $content.Replace($old3, $new3)

$old4 = @'
        _world3DPlaceholder?.SetActive(use3D);
        if (use3D)
'@
$new4 = @'
        _world3DPlaceholder?.SetActive(use3D);
        _worldSelectionRing3D?.SetActive(use3D);
        if (use3D)
'@
if (-not $content.Contains($old4))
{
    throw 'SetActive anchor not found.'
}
$content = $content.Replace($old4, $new4)

$old5 = @'
        _world3DPlaceholder?.SyncDynamicState(_camera3D, _query, _registry, _items, _data, _input, _renderCache, _feedback, _viewport.CurrentZ, visibleTileBounds, _focusedLogTile);
    }
'@
$new5 = @'
        _world3DPlaceholder?.SyncDynamicState(_camera3D, _query, _registry, _items, _data, _input, _renderCache, _feedback, _viewport.CurrentZ, visibleTileBounds, _focusedLogTile);
        _worldSelectionRing3D?.Sync(_query, _input, _map, _viewport.CurrentZ, visibleTileBounds);
    }
'@
if (-not $content.Contains($old5))
{
    throw 'Sync anchor not found.'
}
$content = $content.Replace($old5, $new5)

Set-Content -Path $path -Value $content
