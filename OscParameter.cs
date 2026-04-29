using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace VrcOscSender;

public enum ParamType { Bool, Int, Float }

public class OscParameter : INotifyPropertyChanged
{
    private string _name = "/avatar/parameters/MyParam";
    private ParamType _type = ParamType.Bool;
    private bool _boolValue;
    private int _intValue;
    private float _floatValue;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public ParamType Type
    {
        get => _type;
        set
        {
            _type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBool));
            OnPropertyChanged(nameof(IsInt));
            OnPropertyChanged(nameof(IsFloat));
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set { _boolValue = value; OnPropertyChanged(); }
    }

    public int IntValue
    {
        get => _intValue;
        set { _intValue = Math.Clamp(value, 0, 255); OnPropertyChanged(); }
    }

    // No clamp — allows any float e.g. 1.8 for eye height
    public float FloatValue
    {
        get => _floatValue;
        set { _floatValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(FloatText)); }
    }

    // Editable text representation of the float value
    public string FloatText
    {
        get => _floatValue.ToString("F3", CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                _floatValue = f;
                OnPropertyChanged(nameof(FloatValue));
            }
            OnPropertyChanged();
        }
    }

    public bool IsBool  => Type == ParamType.Bool;
    public bool IsInt   => Type == ParamType.Int;
    public bool IsFloat => Type == ParamType.Float;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
