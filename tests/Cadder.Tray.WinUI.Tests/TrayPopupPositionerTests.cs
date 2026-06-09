using Cadder.Tray.WinUI;

namespace Cadder.Tray.WinUI.Tests;

public sealed class TrayPopupPositionerTests
{
  [Fact]
  public void CalculatePosition_WhenCursorNearBottomRight_OpensAboveAndLeft()
  {
    var position = TrayPopupPositioner.CalculatePosition(
        cursorX: 1900,
        cursorY: 1030,
        popupWidth: 360,
        popupHeight: 480,
        workLeft: 0,
        workTop: 0,
        workRight: 1920,
        workBottom: 1040,
        margin: 8);

    Assert.Equal((1540, 550), position);
  }

  [Fact]
  public void CalculatePosition_WhenCursorNearTopLeft_ClampsToWorkArea()
  {
    var position = TrayPopupPositioner.CalculatePosition(
        cursorX: 4,
        cursorY: 4,
        popupWidth: 360,
        popupHeight: 480,
        workLeft: 0,
        workTop: 0,
        workRight: 1920,
        workBottom: 1040,
        margin: 8);

    Assert.Equal((8, 8), position);
  }
}
