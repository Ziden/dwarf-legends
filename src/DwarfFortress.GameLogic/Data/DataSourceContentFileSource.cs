using System;
using DwarfFortress.GameLogic.Core;
using DwarfFortress.WorldGen.Content;

namespace DwarfFortress.GameLogic.Data;

internal sealed class DataSourceContentFileSource : IContentFileSource
{
    private readonly IDataSource _dataSource;

    public DataSourceContentFileSource(IDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public bool Exists(string path)
        => _dataSource.Exists(path);

    public string ReadText(string path)
        => _dataSource.ReadText(path);

    public IReadOnlyList<string> ListFiles(string directory, string extension = ".json", bool recursive = false)
        => _dataSource.ListFiles(directory, extension, recursive);
}
