using System;
using System.Windows;
using System.Windows.Controls;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Minimal "License &amp; Account" window: shows state, expiry, grace days, truncated
/// licenseId; Activate (key -> manager.Activate) + Refresh. Reads the manager's pinned
/// display accessors only — no logic here. Best-effort: any fault shows a message, never
/// crashes Revit.
/// </summary>
public sealed class LicenseWindow : Window
{
    private readonly TextBlock _stateValue = new TextBlock { FontWeight = FontWeights.Bold };
    private readonly TextBlock _expiryValue = new TextBlock();
    private readonly TextBlock _graceValue = new TextBlock();
    private readonly TextBlock _idValue = new TextBlock();
    private readonly TextBlock _hint = new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly TextBox _keyBox = new TextBox { Margin = new Thickness(0, 2, 0, 8) };

    public LicenseWindow()
    {
        Title = Localization.T("license.window_title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Row(Localization.T("license.state_label"), _stateValue));
        root.Children.Add(Row(Localization.T("license.expiry_label"), _expiryValue));
        root.Children.Add(Row(Localization.T("license.grace_label"), _graceValue));
        root.Children.Add(Row(Localization.T("license.id_label"), _idValue));
        root.Children.Add(_hint);

        root.Children.Add(new TextBlock
        {
            Text = Localization.T("license.key_label"),
            Margin = new Thickness(0, 10, 0, 0),
        });
        root.Children.Add(_keyBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var activate = new Button
        {
            Content = Localization.T("license.activate_button"),
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var refresh = new Button
        {
            Content = Localization.T("license.refresh_button"),
            Width = 110,
            Padding = new Thickness(8, 4, 8, 4),
        };
        activate.Click += OnActivate;
        refresh.Click += OnRefresh;
        buttons.Children.Add(activate);
        buttons.Children.Add(refresh);
        root.Children.Add(buttons);

        Content = root;
        RefreshDisplay();
    }

    private static StackPanel Row(string label, TextBlock value)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(new TextBlock { Text = label, Width = 170 });
        p.Children.Add(value);
        return p;
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        try
        {
            var manager = LicenseBootstrap.Manager;
            if (manager == null)
            {
                MessageBox.Show(Localization.T("license.dev_transparent"), Title);
                return;
            }

            var result = manager.Activate(_keyBox.Text?.Trim() ?? "");
            RefreshDisplay();
            if (result.Success)
                MessageBox.Show(Localization.T("license.activate_ok", StateText(manager.State)), Title);
            else
                MessageBox.Show(Localization.T("license.activate_failed", result.Error ?? ""), Title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localization.T("license.activate_failed", ex.Message), Title);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        try { LicenseBootstrap.Manager?.Refresh(); } catch { }
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var manager = LicenseBootstrap.Manager;
        if (manager == null)
        {
            // Dev or init-failed: transparent.
            _stateValue.Text = StateText(LicenseState.Active);
            _expiryValue.Text = "—";
            _graceValue.Text = "—";
            _idValue.Text = "—";
            _hint.Text = Localization.T("license.dev_transparent");
            return;
        }

        var state = manager.State;
        _stateValue.Text = StateText(state);
        _expiryValue.Text = manager.ExpiresAtUtc?.ToString("yyyy-MM-dd") ?? "—";
        _graceValue.Text = manager.GraceDaysRemaining.ToString();
        _idValue.Text = string.IsNullOrEmpty(manager.LicenseIdTruncated) ? "—" : manager.LicenseIdTruncated;
        _hint.Text = (state == LicenseState.Expired || state == LicenseState.Invalid)
            ? Localization.T("license.expired_hint")
            : "";
    }

    private static string StateText(LicenseState state)
    {
        switch (state)
        {
            case LicenseState.Active:  return Localization.T("license.state_active");
            case LicenseState.Trial:   return Localization.T("license.state_trial");
            case LicenseState.Grace:   return Localization.T("license.state_grace");
            case LicenseState.Expired: return Localization.T("license.state_expired");
            default:                   return Localization.T("license.state_invalid");
        }
    }
}
