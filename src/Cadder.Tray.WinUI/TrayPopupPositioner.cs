namespace Cadder.Tray.WinUI;

internal static class TrayPopupPositioner
{
  public static (int X, int Y) CalculatePosition(
      int cursorX,
      int cursorY,
      int popupWidth,
      int popupHeight,
      int workLeft,
      int workTop,
      int workRight,
      int workBottom,
      int margin)
  {
    var x = cursorX - popupWidth;
    var y = cursorY - popupHeight;

    if (x < workLeft + margin)
    {
      x = cursorX;
    }

    if (y < workTop + margin)
    {
      y = cursorY;
    }

    var maxX = Math.Max(workLeft + margin, workRight - popupWidth - margin);
    var maxY = Math.Max(workTop + margin, workBottom - popupHeight - margin);

    x = Math.Clamp(x, workLeft + margin, maxX);
    y = Math.Clamp(y, workTop + margin, maxY);

    return (x, y);
  }
}
