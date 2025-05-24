using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FocusTracker.Domain.Models;

public class TrackedProgram : INotifyPropertyChanged
{
    public int Id { get; set; }

    private string _name;
    public string Name
    {
        get => _name;
        set { _name = value; RaisePropertyChanged(); }
    }

    private string _identifier;
    public string Identifier
    {
        get => _identifier;
        set { _identifier = value; RaisePropertyChanged(); }
    }

    private string _category;
    public string Category
    {
        get => _category;
        set { _category = value; RaisePropertyChanged(); }
    }

    private bool _isTracked;
    public bool IsTracked
    {
        get => _isTracked;
        set { _isTracked = value; RaisePropertyChanged(); }
    }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set { _isHidden = value; RaisePropertyChanged(); }
    }

    private string? _displayName;
    public string? DisplayName
    {
        get => _displayName;
        set { _displayName = value; RaisePropertyChanged(); }
    }

    private byte[]? _iconBytes;
    public byte[]? IconBytes
    {
        get => _iconBytes;
        set { _iconBytes = value; RaisePropertyChanged(); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public void RaisePropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
