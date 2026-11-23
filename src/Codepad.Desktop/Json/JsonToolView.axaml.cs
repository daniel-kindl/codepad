using Avalonia.Controls;

namespace Codepad.Desktop.Json;

/// <summary>The live JSON tool: the input, the formatted preview, the status line and the result panel.</summary>
public sealed partial class JsonToolView : UserControl
{
    private JsonToolViewModel? model;
    private bool replacingText;

    /// <summary>Initializes a new instance of the <see cref="JsonToolView"/> class.</summary>
    public JsonToolView()
    {
        InitializeComponent();
        InputBox.TextChanged += OnInputChanged;
    }

    /// <summary>Gets the input box. Tests use it to type.</summary>
    internal TextBox Input => InputBox;

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (model is not null)
        {
            model.DocumentLoaded -= OnDocumentLoaded;
        }

        model = DataContext as JsonToolViewModel;
        if (model is not null)
        {
            model.DocumentLoaded += OnDocumentLoaded;
            ShowText(model.Text);
        }
    }

    private void OnDocumentLoaded(string text) => ShowText(text);

    private void ShowText(string text)
    {
        replacingText = true;
        try
        {
            InputBox.Text = text;
        }
        finally
        {
            replacingText = false;
        }
    }

    private void OnInputChanged(object? sender, TextChangedEventArgs e)
    {
        if (!replacingText)
        {
            model?.OnTextEdited(InputBox.Text ?? string.Empty);
        }
    }
}
