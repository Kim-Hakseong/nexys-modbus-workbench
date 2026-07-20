using Avalonia.Controls;

namespace Nmw.App.Views;

/// <summary>내장 슬레이브 시뮬레이터 창 (비모달, 닫아도 시뮬레이터는 계속 동작).</summary>
public sealed partial class SimulatorWindow : Window
{
    /// <summary>시뮬레이터 창을 만든다.</summary>
    public SimulatorWindow()
    {
        InitializeComponent();
    }
}
