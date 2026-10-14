using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace PixPolish.UI;

/// <summary>A Grid whose mouse cursor can be changed (UIElement.ProtectedCursor is protected).</summary>
public sealed class CursorGrid : Grid
{
    public void SetCursor(InputSystemCursorShape shape) => ProtectedCursor = InputSystemCursor.Create(shape);
}
