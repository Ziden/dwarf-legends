$path = '.\src\DwarfFortress.GodotClient\Scripts\Rendering\WorldRender3DPlaceholder.cs'
$content = Get-Content -Path $path -Raw

$pattern1 = '(?s)EnsureChunkMaterial\(\);\r?\n\s*RefreshDirtyChunkSnapshots\(map\);\r?\n\r?\n\s*var activeChunks = map\.AllChunks\(\).*?\r?\n\s*RemoveInactiveChunkMeshes\(activeOrigins\);'
$replacement1 = @'
EnsureChunkMaterial();

        var activeChunks = map.AllChunks()
            .Where(chunk => ChunkContainsWorldZ(chunk.Origin, currentZ) && ChunkIntersectsVisibleTileBounds(chunk.Origin, visibleTileBounds))
            .ToArray();
        var activeOrigins = new HashSet<Vec3i>(activeChunks.Select(chunk => chunk.Origin));

        RemoveInactiveChunkState(activeOrigins);
        RefreshDirtyChunkSnapshots(activeChunks);
'@
$newContent = [regex]::Replace($content, $pattern1, $replacement1, 1)
if ($newContent -eq $content)
{
    throw 'SyncSlice replacement failed.'
}
$content = $newContent

$pattern2 = '(?s)    private void RefreshDirtyChunkSnapshots\(WorldMap map\)\r?\n    \{.*?\r?\n    \}'
$replacement2 = @'
    private void RefreshDirtyChunkSnapshots(IEnumerable<Chunk> activeChunks)
    {
        foreach (var chunk in activeChunks)
        {
            if (!chunk.IsDirty)
                continue;

            _chunkSnapshots[chunk.Origin] = WorldChunkRenderSnapshot.Capture(chunk, _nextSnapshotVersion++);
            chunk.ClearDirty();
        }
    }
'@
$content = [regex]::Replace($content, $pattern2, $replacement2, 1)

$pattern3 = '(?s)    private void RemoveInactiveChunkMeshes\(HashSet<Vec3i> activeOrigins\)\r?\n    \{.*?\r?\n    \}'
$replacement3 = @'
    private void RemoveInactiveChunkState(HashSet<Vec3i> activeOrigins)
    {
        if (_chunkMeshes.Count > 0)
        {
            var staleMeshOrigins = _chunkMeshes.Keys.Where(origin => !activeOrigins.Contains(origin)).ToArray();
            foreach (var origin in staleMeshOrigins)
                RemoveChunkMesh(origin);
        }

        if (_chunkSnapshots.Count == 0)
            return;

        var staleSnapshotOrigins = _chunkSnapshots.Keys.Where(origin => !activeOrigins.Contains(origin)).ToArray();
        foreach (var origin in staleSnapshotOrigins)
            _chunkSnapshots.Remove(origin);
    }
'@
$content = [regex]::Replace($content, $pattern3, $replacement3, 1)

Set-Content -Path $path -Value $content
