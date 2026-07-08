using System.Numerics;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Holds a queue of pending construction tasks for a worker unit, to be executed sequentially after the current task completes.
/// </summary>
internal struct BuildQueue
{
    private const int MaxCapacity = 8;

    private int _count;
    private string? _type0; private Vector3 _pos0;
    private string? _type1; private Vector3 _pos1;
    private string? _type2; private Vector3 _pos2;
    private string? _type3; private Vector3 _pos3;
    private string? _type4; private Vector3 _pos4;
    private string? _type5; private Vector3 _pos5;
    private string? _type6; private Vector3 _pos6;
    private string? _type7; private Vector3 _pos7;

    public readonly int Count => _count;

    public const int Capacity = MaxCapacity;

    public bool TryEnqueue(string type, Vector3 position)
    {
        if (_count >= MaxCapacity) return false;
        SetSlot(_count, type, position);
        _count++;
        return true;
    }

    public void PeekAt(int index, out string? type, out Vector3 position)
    {
        if (index < 0 || index >= _count)
        {
            type = null;
            position = default;
            return;
        }
        GetSlot(index, out type, out position);
    }

    public bool TryDequeue(out string? type, out Vector3 position)
    {
        if (_count == 0)
        {
            type = null;
            position = default;
            return false;
        }
        GetSlot(0, out type, out position);
        for (int slotIndex = 0; slotIndex < _count - 1; slotIndex++)
        {
            GetSlot(slotIndex + 1, out string? nextType, out Vector3 nextPos);
            SetSlot(slotIndex, nextType, nextPos);
        }
        SetSlot(_count - 1, null, default);
        _count--;
        return true;
    }

    private void SetSlot(int index, string? type, Vector3 position)
    {
        switch (index)
        {
            case 0: _type0 = type; _pos0 = position; break;
            case 1: _type1 = type; _pos1 = position; break;
            case 2: _type2 = type; _pos2 = position; break;
            case 3: _type3 = type; _pos3 = position; break;
            case 4: _type4 = type; _pos4 = position; break;
            case 5: _type5 = type; _pos5 = position; break;
            case 6: _type6 = type; _pos6 = position; break;
            case 7: _type7 = type; _pos7 = position; break;
        }
    }

    private void GetSlot(int index, out string? type, out Vector3 position)
    {
        switch (index)
        {
            case 0: type = _type0; position = _pos0; break;
            case 1: type = _type1; position = _pos1; break;
            case 2: type = _type2; position = _pos2; break;
            case 3: type = _type3; position = _pos3; break;
            case 4: type = _type4; position = _pos4; break;
            case 5: type = _type5; position = _pos5; break;
            case 6: type = _type6; position = _pos6; break;
            case 7: type = _type7; position = _pos7; break;
            default: type = null; position = default; break;
        }
    }
}
