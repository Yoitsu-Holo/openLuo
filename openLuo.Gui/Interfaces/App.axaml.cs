using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace openLuo.Interfaces.GUI;

public sealed class App : Avalonia.Application
{
    public override void Initialize() { }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new GuiMainViewModel(GuiApplication.Runtime ?? throw new InvalidOperationException("GUI runtime is not configured."));
            var output = new Avalonia.Controls.TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var input = new Avalonia.Controls.TextBox { Watermark = "输入消息，Enter 发送" };
            var send = new Avalonia.Controls.Button { Content = "发送" };
            async Task Submit()
            {
                var text = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text)) return;
                input.Text = string.Empty;
                await viewModel.SendAsync(text);
                output.Text = string.Join("\n", viewModel.Messages);
            }
            send.Click += async (_, _) => await Submit();
            input.KeyDown += async (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) { args.Handled = true; await Submit(); } };
            desktop.MainWindow = new Avalonia.Controls.Window
            {
                Title = "openLuo", Width = 900, Height = 620,
                Content = new Avalonia.Controls.Grid
                {
                    RowDefinitions = new Avalonia.Controls.RowDefinitions("*,Auto"),
                    Children = { output, new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, [Avalonia.Controls.Grid.RowProperty] = 1, Children = { input, send } } }
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
