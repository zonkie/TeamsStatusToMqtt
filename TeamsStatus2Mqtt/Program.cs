using System.Drawing.Printing;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace TeamsStatus2Mqtt;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly TeamsPresenceService _presenceService;
    private readonly MqttPresencePublisher _mqttPublisher;

    private CancellationTokenSource? _pollingCancellationTokenSource;

    private readonly TextBox _tenantIdTextBox = new();
    private readonly TextBox _clientIdTextBox = new();
    private readonly TextBox _mqttHostTextBox = new();
    private readonly NumericUpDown _mqttPortNumeric = new();
    private readonly TextBox _mqttTopicTextBox = new();
    private readonly TextBox _mqttUsernameTextBox = new();
    private readonly TextBox _mqttPasswordTextBox = new();
    private readonly NumericUpDown _pollIntervalNumeric = new();
    private readonly CheckBox _mqttUseTlsCheckBox = new();

    private readonly Button _saveButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _testOnceButton = new();
    private readonly Button _infoButton = new();

    private readonly Label _currentStatusLabel = new();
    private readonly TextBox _logTextBox = new();

    public MainForm()
    {
        Text = "Teams Status to MQTT";
        Width = 860;
        Height = 680;
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterScreen;

        _config = AppConfigStore.Load();
        _presenceService = new TeamsPresenceService(Log);
        _mqttPublisher = new MqttPresencePublisher(Log);

        BuildUi();
        LoadConfigIntoUi();
        UpdateButtons(isRunning: false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollingCancellationTokenSource?.Cancel();
        _pollingCancellationTokenSource?.Dispose();

        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var menuStrip = new MenuStrip();

        var infoMenuItem = new ToolStripMenuItem("Info");
        var azureSetupMenuItem = new ToolStripMenuItem("Azure App Registration Setup");

        azureSetupMenuItem.Click += (_, _) => ShowAzureAppRegistrationInfo();

        infoMenuItem.DropDownItems.Add(azureSetupMenuItem);
        menuStrip.Items.Add(infoMenuItem);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(root);
        root.BringToFront();

        var configGroup = new GroupBox
        {
            Text = "Configuration",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 9,
            AutoSize = true
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        configGroup.Controls.Add(grid);

        AddRow(grid, 0, "Azure Tenant ID", _tenantIdTextBox);
        AddRow(grid, 1, "Azure Client ID", _clientIdTextBox);
        AddRow(grid, 2, "MQTT Host", _mqttHostTextBox);

        _mqttPortNumeric.Minimum = 1;
        _mqttPortNumeric.Maximum = 65535;
        AddRow(grid, 3, "MQTT Port", _mqttPortNumeric);

        AddRow(grid, 4, "MQTT Topic", _mqttTopicTextBox);
        AddRow(grid, 5, "MQTT Username", _mqttUsernameTextBox);

        _mqttPasswordTextBox.UseSystemPasswordChar = true;
        AddRow(grid, 6, "MQTT Password", _mqttPasswordTextBox);

        _mqttUseTlsCheckBox.Text = "Use TLS";
        AddRow(grid, 7, "MQTT TLS", _mqttUseTlsCheckBox);

        _pollIntervalNumeric.Minimum = 5;
        _pollIntervalNumeric.Maximum = 3600;
        AddRow(grid, 8, "Poll Interval Seconds", _pollIntervalNumeric);

        root.Controls.Add(configGroup, 0, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 10),
        };

        _saveButton.Text = "Save Config";
        _saveButton.Width = 120;
        _saveButton.Click += (_, _) => SaveConfigFromUi();

        _testOnceButton.Text = "Test Once";
        _testOnceButton.Width = 120;
        _testOnceButton.Click += async (_, _) => await TestOnceAsync();

        _startButton.Text = "Start";
        _startButton.Width = 120;
        _startButton.Click += async (_, _) => await StartAsync();

        _stopButton.Text = "Stop";
        _stopButton.Width = 120;
        _stopButton.Click += (_, _) => Stop();
        
        _infoButton.Text = "Info";
        _infoButton.Width = 120;
        _infoButton.Click += (_, _) => ShowAzureAppRegistrationInfo();
        

        buttonPanel.Controls.Add(_saveButton);
        buttonPanel.Controls.Add(_testOnceButton);
        buttonPanel.Controls.Add(_startButton);
        buttonPanel.Controls.Add(_stopButton);
        buttonPanel.Controls.Add(_infoButton);

        root.Controls.Add(buttonPanel, 0, 1);

        var statusPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50
        };

        _currentStatusLabel.Text = "Current status: unknown";
        _currentStatusLabel.Dock = DockStyle.Fill;
        _currentStatusLabel.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        _currentStatusLabel.TextAlign = ContentAlignment.MiddleLeft;

        statusPanel.Controls.Add(_currentStatusLabel);
        root.Controls.Add(statusPanel, 0, 2);

        var logGroup = new GroupBox
        {
            Text = "Log",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.ReadOnly = true;
        _logTextBox.Font = new Font("Consolas", 9);

        logGroup.Controls.Add(_logTextBox);
        root.Controls.Add(logGroup, 0, 3);
    }

    private void ShowAzureAppRegistrationInfo()
    {
        const string message = """
        Azure App Registration setup:

        1. Open the Azure Portal.
        2. Go to Microsoft Entra ID.
        3. Open App registrations.
        4. Click New registration.
        5. Choose a name, for example: TeamsStatus2Mqtt.
        6. Select the supported account type you need.
           For most company-internal usage, choose Single tenant.
        7. Create the app registration.
        8. Copy the following values into this app:
           - Directory tenant ID -> Azure Tenant ID
           - Application client ID -> Azure Client ID

        Authentication settings:

        9. In the app registration, open Authentication.
        10. Enable:
            Allow public client flows

        Microsoft Graph permissions:

        11. Open API permissions.
        12. Add Microsoft Graph delegated permissions:
            - User.Read
            - Presence.Read
        13. If your tenant requires it, click Grant admin consent.

        After this, click Save Config in this app and then Test Once.
        The first Graph request will show a device-code login prompt.
        """;

        MessageBox.Show(
            this,
            message,
            "Azure App Registration Setup",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 6)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 3, 0, 3);

        grid.Controls.Add(labelControl, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void LoadConfigIntoUi()
    {
        _tenantIdTextBox.Text = _config.AzureTenantId;
        _clientIdTextBox.Text = _config.AzureClientId;

        _mqttHostTextBox.Text = _config.MqttHost;
        _mqttPortNumeric.Value = _config.MqttPort;
        _mqttTopicTextBox.Text = _config.MqttTopic;
        _mqttUsernameTextBox.Text = _config.MqttUsername;
        _mqttPasswordTextBox.Text = _config.MqttPassword;
        _mqttUseTlsCheckBox.Checked = _config.MqttUseTls;

        _pollIntervalNumeric.Value = _config.PollIntervalSeconds;
    }

    private bool SaveConfigFromUi()
    {
        _config.AzureTenantId = _tenantIdTextBox.Text.Trim();
        _config.AzureClientId = _clientIdTextBox.Text.Trim();

        _config.MqttHost = _mqttHostTextBox.Text.Trim();
        _config.MqttPort = (int)_mqttPortNumeric.Value;
        _config.MqttTopic = _mqttTopicTextBox.Text.Trim();
        _config.MqttUsername = _mqttUsernameTextBox.Text.Trim();
        _config.MqttPassword = _mqttPasswordTextBox.Text;
        _config.MqttUseTls = _mqttUseTlsCheckBox.Checked;

        _config.PollIntervalSeconds = (int)_pollIntervalNumeric.Value;

        var validationError = _config.Validate();
        if (validationError is not null)
        {
            MessageBox.Show(this, validationError, "Invalid configuration", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        AppConfigStore.Save(_config);
        Log("Configuration saved.");
        return true;
    }

    private async Task TestOnceAsync()
    {
        if (!SaveConfigFromUi())
        {
            return;
        }

        try
        {
            UpdateButtons(isRunning: true);

            var presence = await _presenceService.GetPresenceAsync(_config, CancellationToken.None);
            await _mqttPublisher.PublishAsync(_config, presence, CancellationToken.None);

            UpdateCurrentStatus(presence);
            Log($"Test publish completed. Availability={presence.Availability}, Activity={presence.Activity}");
        }
        catch (Exception ex)
        {
            LogException("Test failed", ex);
            MessageBox.Show(this, ex.Message, "Test failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateButtons(isRunning: false);
        }
    }

    private async Task StartAsync()
    {
        if (!SaveConfigFromUi())
        {
            return;
        }

        if (_pollingCancellationTokenSource is not null)
        {
            return;
        }

        _pollingCancellationTokenSource = new CancellationTokenSource();
        UpdateButtons(isRunning: true);

        Log("Started polling Teams presence.");

        try
        {
            await Task.Run(() => PollLoopAsync(_pollingCancellationTokenSource.Token));
        }
        catch (OperationCanceledException)
        {
            Log("Polling stopped.");
        }
        catch (Exception ex)
        {
            LogException("Polling failed", ex);
            MessageBox.Show(this, ex.Message, "Polling failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _pollingCancellationTokenSource?.Dispose();
            _pollingCancellationTokenSource = null;
            UpdateButtons(isRunning: false);
        }
    }

    private void Stop()
    {
        _pollingCancellationTokenSource?.Cancel();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        string? lastPayload = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var presence = await _presenceService.GetPresenceAsync(_config, cancellationToken);
                var payload = presence.ToJson();

                if (!string.Equals(payload, lastPayload, StringComparison.Ordinal))
                {
                    await _mqttPublisher.PublishAsync(_config, presence, cancellationToken);
                    lastPayload = payload;

                    BeginInvoke(() => UpdateCurrentStatus(presence));
                    Log(
                        $"Published Teams presence. Availability={presence.Availability}, Activity={presence.Activity}");
                }
                else
                {
                    Log($"No status change. Availability={presence.Availability}, Activity={presence.Activity}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogException("Polling iteration failed", ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), cancellationToken);
        }
    }

    private void UpdateCurrentStatus(TeamsPresence presence)
    {
        _currentStatusLabel.Text = $"Current status: {presence.Availability} / {presence.Activity}";
    }

    private void UpdateButtons(bool isRunning)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateButtons(isRunning));
            return;
        }

        _saveButton.Enabled = !isRunning;
        _testOnceButton.Enabled = !isRunning;
        _startButton.Enabled = !isRunning;
        _stopButton.Enabled = isRunning;
    }

    private void Log(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        _logTextBox.AppendText($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void LogException(string message, Exception exception)
    {
        Log($"{message}: {exception.GetType().Name}: {exception.Message}");
    }
}

public sealed class AppConfig
{
    public string AzureTenantId { get; set; } = "";
    public string AzureClientId { get; set; } = "";

    public string MqttHost { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;
    public string MqttTopic { get; set; } = "teams/status";
    public string MqttUsername { get; set; } = "";
    public string MqttPassword { get; set; } = "";
    public bool MqttUseTls { get; set; }

    public int PollIntervalSeconds { get; set; } = 30;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(AzureTenantId))
        {
            return "Azure Tenant ID is required.";
        }

        if (string.IsNullOrWhiteSpace(AzureClientId))
        {
            return "Azure Client ID is required.";
        }

        if (string.IsNullOrWhiteSpace(MqttHost))
        {
            return "MQTT Host is required.";
        }

        if (MqttPort is < 1 or > 65535)
        {
            return "MQTT Port must be between 1 and 65535.";
        }

        if (string.IsNullOrWhiteSpace(MqttTopic))
        {
            return "MQTT Topic is required.";
        }

        if (PollIntervalSeconds < 5)
        {
            return "Poll interval must be at least 5 seconds.";
        }

        return null;
    }
}

public static class AppConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}

public sealed class TeamsPresenceService
{
    private static readonly string[] Scopes =
    [
        "User.Read",
        "Presence.Read"
    ];

    private readonly HttpClient _httpClient = new();
    private readonly Action<string> _log;

    private IPublicClientApplication? _publicClientApplication;
    private string? _currentTenantId;
    private string? _currentClientId;

    public TeamsPresenceService(Action<string> log)
    {
        _log = log;
    }

    public async Task<TeamsPresence> GetPresenceAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var accessToken = await GetAccessTokenAsync(config, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/presence");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Graph API call failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        return new TeamsPresence
        {
            Availability = GetOptionalString(root, "availability"),
            Activity = GetOptionalString(root, "activity"),
            RawJson = responseBody,
            TimestampUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<string> GetAccessTokenAsync(AppConfig config, CancellationToken cancellationToken)
    {
        EnsureMsalClient(config);

        var accounts = await _publicClientApplication!.GetAccountsAsync();
        var account = accounts.FirstOrDefault();

        try
        {
            if (account is not null)
            {
                var silentResult = await _publicClientApplication
                    .AcquireTokenSilent(Scopes, account)
                    .ExecuteAsync(cancellationToken);

                return silentResult.AccessToken;
            }
        }
        catch (MsalUiRequiredException)
        {
            // Interactive device-code login below.
        }

        _log("Authentication required. Follow the device-code instructions shown below.");

        var result = await _publicClientApplication
            .AcquireTokenWithDeviceCode(Scopes, deviceCode =>
            {
                _log(deviceCode.Message);
                MessageBox.Show(deviceCode.Message, "Microsoft Graph Login", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return Task.CompletedTask;
            })
            .ExecuteAsync(cancellationToken);

        return result.AccessToken;
    }

    private void EnsureMsalClient(AppConfig config)
    {
        if (_publicClientApplication is not null &&
            string.Equals(_currentTenantId, config.AzureTenantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_currentClientId, config.AzureClientId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentTenantId = config.AzureTenantId;
        _currentClientId = config.AzureClientId;

        _publicClientApplication = PublicClientApplicationBuilder
            .Create(config.AzureClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, config.AzureTenantId)
            .WithDefaultRedirectUri()
            .Build();
    }

    private static string GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }
}

public sealed class MqttPresencePublisher
{
    private readonly Action<string> _log;

    public MqttPresencePublisher(Action<string> log)
    {
        _log = log;
    }

    public async Task PublishAsync(AppConfig config, TeamsPresence presence, CancellationToken cancellationToken)
    {
        var factory = new MqttFactory();

        using var client = factory.CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(config.MqttHost, config.MqttPort)
            .WithClientId($"teams-status-2-mqtt-{Environment.MachineName}-{Environment.ProcessId}")
            .WithCleanSession();

        if (!string.IsNullOrWhiteSpace(config.MqttUsername))
        {
            builder = builder.WithCredentials(config.MqttUsername, config.MqttPassword);
        }

        if (config.MqttUseTls)
        {
            builder = builder.WithTlsOptions(options => { options.UseTls(); });
        }

        var options = builder.Build();

        await client.ConnectAsync(options, cancellationToken);

        var payload = presence.ToJson();

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(config.MqttTopic)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build();

        await client.PublishAsync(message, cancellationToken);
        await client.DisconnectAsync(cancellationToken: cancellationToken);

        _log($"MQTT message published to topic '{config.MqttTopic}'.");
    }
}

public sealed class TeamsPresence
{
    public string Availability { get; init; } = "";
    public string Activity { get; init; } = "";
    public string RawJson { get; init; } = "";
    public DateTimeOffset TimestampUtc { get; init; }

    public string ToJson()
    {
        var payload = new
        {
            availability = Availability,
            activity = Activity,
            timestampUtc = TimestampUtc,
            source = "TeamsStatus2Mqtt",
            rawGraphPresence = TryParseRawJson(RawJson)
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    private static JsonElement? TryParseRawJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}