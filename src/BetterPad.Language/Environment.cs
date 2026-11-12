namespace BetterPad.Language;

internal sealed class Environment
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private readonly Environment? _parent;

    public Environment(Environment? parent)
    {
        _parent = parent;
    }

    public bool TryGet(string name, out object value)
    {
        if (_values.TryGetValue(name, out object? found))
        {
            value = found;
            return true;
        }

        if (_parent != null)
        {
            return _parent.TryGet(name, out value);
        }

        value = null!;
        return false;
    }

    public bool TryAssign(string name, object value)
    {
        if (_values.ContainsKey(name))
        {
            _values[name] = value;
            return true;
        }

        return _parent != null && _parent.TryAssign(name, value);
    }

    public bool ContainsLocal(string name) => _values.ContainsKey(name);

    public void Declare(string name, object value) => _values[name] = value;
}
