using System.Windows;
using GenshinChatTranslator.App.Localization;
using GenshinChatTranslator.App.Translation;
using GenshinChatTranslator.App.Translation.Engines;

namespace GenshinChatTranslator.App;

public partial class LlmConfigurationDialog : Window
{
    private readonly string _translationConfigPath;
    private readonly TranslationOptions _translationOptions;

    public LlmConfigurationDialog(string translationConfigPath, TranslationOptions translationOptions)
    {
        InitializeComponent();

        _translationConfigPath = translationConfigPath;
        _translationOptions = translationOptions;

        EndpointTextBox.Text = translationOptions.OpenAiCompatibleLlm.Endpoint;
        ModelTextBox.Text = translationOptions.OpenAiCompatibleLlm.Model;
        StatusTextBlock.Text = HasUsableApiKey(translationOptions.OpenAiCompatibleLlm)
            ? LocalizationManager.Text("LlmConfigApiKeyConfigured")
            : LocalizationManager.Text("LlmConfigApiKeyMissing");
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = EndpointTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim();
        var newApiKey = string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password)
            ? null
            : ApiKeyPasswordBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            StatusTextBlock.Text = LocalizationManager.Text("LlmConfigRequiredFieldsMissing");
            return;
        }

        SetBusy(true);
        StatusTextBlock.Text = LocalizationManager.Text("LlmConfigValidating");
        try
        {
            var options = _translationOptions.OpenAiCompatibleLlm with
            {
                Endpoint = endpoint,
                Model = model,
                ApiKey = newApiKey ?? _translationOptions.OpenAiCompatibleLlm.ApiKey,
            };
            var validation = await OpenAiCompatibleLlmTranslateEngine.ValidateConnectionAsync(
                options,
                _translationOptions.TimeoutMs,
                CancellationToken.None);
            if (!validation.IsSuccess)
            {
                StatusTextBlock.Text = LocalizationManager.Format("LlmConfigValidationFailedFormat", validation.ErrorMessage);
                return;
            }

            OpenAiCompatibleLlmConfigWriter.Save(_translationConfigPath, endpoint, model, newApiKey);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationManager.Format("LlmConfigValidationFailedFormat", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetBusy(bool isBusy)
    {
        EndpointTextBox.IsEnabled = !isBusy;
        ApiKeyPasswordBox.IsEnabled = !isBusy;
        ModelTextBox.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = !isBusy;
    }

    private static bool HasUsableApiKey(OpenAiCompatibleLlmTranslationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.ApiKey) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    }
}
