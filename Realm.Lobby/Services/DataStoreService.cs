using DBreeze;
using System.Text.Json;

namespace Realm.Lobby.Services;

public class DataStoreService : IDisposable
{
    private readonly DBreezeEngine _engine;

    public DataStoreService()
    {
        Directory.CreateDirectory(".data");
        _engine = new DBreezeEngine(".data");
    }

    public T? Get<T>(string collection, string id)
    {
        using var t = _engine.GetTransaction();
        var row = t.Select<string, string>(collection, id);
        if (row.Exists)
        {
            return JsonSerializer.Deserialize<T>(row.Value);
        }
        return default;
    }

    public IEnumerable<T> GetAll<T>(string collection)
    {
        using var t = _engine.GetTransaction();
        var list = new List<T>();
        foreach (var row in t.SelectForward<string, string>(collection))
        {
            var item = JsonSerializer.Deserialize<T>(row.Value);
            if (item != null)
            {
                list.Add(item);
            }
        }
        return list;
    }

    public void Upsert<T>(string collection, string id, T data)
    {
        using var t = _engine.GetTransaction();
        t.SynchronizeTables(collection);
        var json = JsonSerializer.Serialize(data);
        t.Insert(collection, id, json);
        t.Commit();
    }

    public void Delete(string collection, string id)
    {
        using var t = _engine.GetTransaction();
        t.SynchronizeTables(collection);
        t.RemoveKey(collection, id);
        t.Commit();
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
