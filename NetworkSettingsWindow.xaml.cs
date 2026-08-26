using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using RohreZuschnittOptimierung.Models;
using RohreZuschnittOptimierung.Services;

namespace RohreZuschnittOptimierung;

public partial class NetworkSettingsWindow : Window
{
  public NetworkSettingsWindow()
  {
    InitializeComponent();
    Loaded += (_, _) =>
    {
      WindowChromeService.ApplyTheme(this, ThemeService.IsDarkMode);
      LoadSettings();
    };
  }

  private void LoadSettings()
  {
    var appSettings = AppSettingsStore.Load();
    WarehouseSyncModeComboBox.SelectedIndex = appSettings.WarehouseSyncMode?.ToLowerInvariant() switch
    {
      "host" => 1,
      "client" => 2,
      _ => 0
    };
    WarehouseHubPortTextBox.Text = (appSettings.WarehouseHubPort > 0 ? appSettings.WarehouseHubPort : 5088)
      .ToString(CultureInfo.InvariantCulture);
    WarehouseHubUrlTextBox.Text = appSettings.WarehouseHubUrl ?? string.Empty;
    UpdateWarehouseHubUi();
  }

  private void WarehouseSyncModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (!IsLoaded)
      return;
    UpdateWarehouseHubUi();
  }

  private void WarehouseHubPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
  {
    if (!IsLoaded)
      return;
    if (WarehouseSyncModeComboBox.SelectedIndex == 1)
      RefreshHostAddressDisplay();
  }

  private void UpdateWarehouseHubUi()
  {
    var mode = WarehouseSyncModeComboBox.SelectedIndex;
    HostAddressPanel.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
    ClientAddressLabel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
    ClientAddressPanel.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;

    if (mode == 1)
      RefreshHostAddressDisplay();

    WarehouseHubStatusTextBlock.Text = mode switch
    {
      1 => BuildHostStatusText(),
      2 => "Client: Lager liegt nur auf der Zentrale. Adresse oben eintragen und „Prüfen“.",
      _ => "Lokal: SQLite neben der EXE (Daten\\pipe-warehouse.db)"
    };
  }

  private void RefreshHostAddressDisplay()
  {
    var urls = GetLocalHubUrls(GetSelectedPort());
    HostAddressTextBox.Text = urls.Count > 0 ? urls[0] : $"http://127.0.0.1:{GetSelectedPort()}";
    HostAddressHintTextBlock.Text = urls.Count > 1
      ? "Weitere Adressen dieses PCs: " + string.Join("  ·  ", urls.Skip(1))
        + Environment.NewLine + "Adresse kopieren und auf den anderen PCs eintragen. Firewall: Port freigeben."
      : "Adresse kopieren und auf den anderen PCs unter „Mit Lager-Zentrale verbinden“ eintragen. Firewall: Port freigeben.";
  }

  private string BuildHostStatusText()
  {
    var urls = GetLocalHubUrls(GetSelectedPort());
    if (urls.Count == 0)
      return "Host: keine LAN-IP gefunden – bitte Netzwerk prüfen.";
    return "Host aktiv nach Speichern. Andere PCs nutzen: " + urls[0];
  }

  private int GetSelectedPort()
  {
    if (int.TryParse(WarehouseHubPortTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
        && port is >= 1 and <= 65535)
      return port;
    return 5088;
  }

  private static List<string> GetLocalHubUrls(int port)
  {
    var urls = new List<string>();
    try
    {
      foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
      {
        if (address.AddressFamily != AddressFamily.InterNetwork)
          continue;
        if (IPAddress.IsLoopback(address))
          continue;
        var text = address.ToString();
        if (text.StartsWith("169.254.", StringComparison.Ordinal))
          continue;
        urls.Add($"http://{text}:{port}");
      }
    }
    catch
    {
    }

    if (urls.Count == 0)
      urls.Add($"http://127.0.0.1:{port}");

    return urls
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(u => u.Contains("192.168.", StringComparison.Ordinal) ? 0 : 1)
      .ThenBy(u => u, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private void CopyHostAddress_Click(object sender, RoutedEventArgs e)
  {
    var text = HostAddressTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(text))
      return;
    Clipboard.SetText(text);
    WarehouseHubStatusTextBlock.Text = "Kopiert: " + text;
  }

  private async void ProbeWarehouseHub_Click(object sender, RoutedEventArgs e)
  {
    var url = WarehouseHubUrlTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(url))
    {
      MessageBox.Show(this, "Bitte die Adresse der Lager-Zentrale eintragen.", "Netzwerk",
        MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    WarehouseHubStatusTextBlock.Text = "Prüfe Verbindung…";
    var (ok, message) = await WarehouseHubClient.ProbeAsync(url).ConfigureAwait(true);
    WarehouseHubStatusTextBlock.Text = message;
    if (!ok)
      MessageBox.Show(this, message, "Netzwerk", MessageBoxButton.OK, MessageBoxImage.Warning);
  }

  private void Save_Click(object sender, RoutedEventArgs e)
  {
    if (!int.TryParse(WarehouseHubPortTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hubPort)
        || hubPort is < 1 or > 65535)
    {
      MessageBox.Show(this, "Bitte einen gültigen Port (1–65535) eingeben.", "Netzwerk",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      WarehouseHubPortTextBox.Focus();
      return;
    }

    var mode = WarehouseSyncModeComboBox.SelectedIndex switch
    {
      1 => nameof(WarehouseSyncMode.Host),
      2 => nameof(WarehouseSyncMode.Client),
      _ => nameof(WarehouseSyncMode.Local)
    };

    var hubUrl = WarehouseHubClient.NormalizeBaseUrl(WarehouseHubUrlTextBox.Text);
    if (mode == nameof(WarehouseSyncMode.Host))
      hubUrl = HostAddressTextBox.Text.Trim();

    if (mode == nameof(WarehouseSyncMode.Client) && string.IsNullOrWhiteSpace(hubUrl))
    {
      MessageBox.Show(this, "Im Client-Modus bitte die Adresse der Lager-Zentrale eintragen.", "Netzwerk",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      WarehouseHubUrlTextBox.Focus();
      return;
    }

    var existing = AppSettingsStore.Load();
    existing.WarehouseSyncMode = mode;
    existing.WarehouseHubPort = hubPort;
    existing.WarehouseHubUrl = hubUrl;
    AppSettingsStore.Save(existing);

    try
    {
      PipeWarehouseStore.ApplyRuntimeMode(existing);
    }
    catch (Exception ex)
    {
      MessageBox.Show(this, "Lager-Modus konnte nicht gestartet werden:\n" + ex.Message, "Netzwerk",
        MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    DialogResult = true;
    Close();
  }

  private void Close_Click(object sender, RoutedEventArgs e)
  {
    DialogResult = false;
    Close();
  }
}
