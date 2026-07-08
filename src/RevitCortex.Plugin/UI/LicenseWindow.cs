using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// "License &amp; Account" window: shows state, expiry, grace days, truncated
/// licenseId; Activate (key -> manager.Activate) + Refresh. Reads the manager's pinned
/// display accessors only — no logic here. Best-effort: any fault shows a message, never
/// crashes Revit. Visual style mirrors RevitCortex.Plugin.UI.GeneralSettingsPage
/// (teal accent, status banner with colored dot, row/separator layout).
/// </summary>
public sealed class LicenseWindow : Window
{
    private static readonly Brush TextPrimary = Brush(51, 51, 51);     // #333
    private static readonly Brush TextSecondary = Brush(102, 102, 102); // #666
    private static readonly Brush BorderLight = Brush(224, 224, 224);   // #E0E0E0
    private static readonly Brush PanelBackground = Brush(245, 245, 245); // #F5F5F5
    private static readonly Brush TealAccent = Brush(0, 131, 143);      // #00838F
    private static readonly Brush TealDark = Brush(0, 96, 100);         // #006064
    private static readonly Brush ActiveGreen = Brush(46, 125, 50);     // #2E7D32
    private static readonly Brush GraceAmber = Brush(255, 143, 0);      // #FF8F00
    private static readonly Brush ExpiredRed = Brush(198, 40, 40);      // #C62828

    private readonly Ellipse _statusDot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _statusTitle = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary };
    private readonly TextBlock _statusDetail = new TextBlock { FontSize = 11, Foreground = TextSecondary, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _expiryValue = new TextBlock { FontSize = 14, Foreground = TextPrimary };
    private readonly TextBlock _graceValue = new TextBlock { FontSize = 14, Foreground = TextPrimary };
    private readonly TextBlock _idValue = new TextBlock { FontSize = 14, Foreground = TextPrimary };
    private readonly TextBlock _hint = new TextBlock
    {
        FontSize = 12,
        Foreground = TextSecondary,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
    };
    private readonly TextBox _keyBox = new TextBox
    {
        Padding = new Thickness(8, 6, 8, 6),
        FontSize = 14,
        BorderBrush = BorderLight,
        Margin = new Thickness(0, 6, 0, 12),
    };

    public LicenseWindow()
    {
        Title = Localization.T("license.window_title");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;

        var root = new StackPanel { Margin = new Thickness(20) };

        // Header (matches GeneralSettingsPage title block)
        root.Children.Add(new TextBlock
        {
            Text = Localization.T("license.window_title"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
        });
        root.Children.Add(new TextBlock
        {
            Text = Localization.T("license.header_subtitle"),
            Foreground = TextSecondary,
            Margin = new Thickness(0, 5, 0, 15),
        });

        // Status banner (dot + title/detail), same shape as the connection status banner
        var banner = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 15),
            Background = PanelBackground,
            BorderThickness = new Thickness(1),
            BorderBrush = BorderLight,
        };
        var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
        bannerRow.Children.Add(_statusDot);
        var bannerText = new StackPanel();
        bannerText.Children.Add(_statusTitle);
        bannerText.Children.Add(_statusDetail);
        bannerRow.Children.Add(bannerText);
        banner.Child = bannerRow;
        root.Children.Add(banner);

        // Detail rows with separators, matching the Settings form layout
        root.Children.Add(Row(Localization.T("license.expiry_label"), _expiryValue));
        root.Children.Add(Separator());
        root.Children.Add(Row(Localization.T("license.grace_label"), _graceValue));
        root.Children.Add(Separator());
        root.Children.Add(Row(Localization.T("license.id_label"), _idValue));
        root.Children.Add(Separator());

        root.Children.Add(_hint);

        root.Children.Add(new TextBlock
        {
            Text = Localization.T("license.key_label"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 14, 0, 0),
        });
        root.Children.Add(_keyBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var refresh = new Button
        {
            Content = Localization.T("license.refresh_button"),
            Padding = new Thickness(15, 8, 15, 8),
            FontSize = 13,
            Background = PanelBackground,
            Foreground = TextPrimary,
            BorderBrush = BorderLight,
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var activate = new Button
        {
            Content = Localization.T("license.activate_button"),
            Padding = new Thickness(20, 8, 20, 8),
            FontSize = 13,
            Background = TealAccent,
            Foreground = Brushes.White,
            BorderBrush = TealDark,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        activate.Click += OnActivate;
        refresh.Click += OnRefresh;
        buttons.Children.Add(refresh);
        buttons.Children.Add(activate);
        root.Children.Add(buttons);

        Content = root;
        RefreshDisplay();
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Separator Separator() => new Separator { Margin = new Thickness(0, 4, 0, 4), Background = BorderLight };

    private static Grid Row(string label, TextBlock value)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(value, 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(labelBlock);
        grid.Children.Add(value);
        return grid;
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
            SetBanner(LicenseState.Active, StateText(LicenseState.Active));
            _expiryValue.Text = "—";
            _graceValue.Text = "—";
            _idValue.Text = "—";
            _hint.Text = Localization.T("license.dev_transparent");
            return;
        }

        var state = manager.State;
        SetBanner(state, StateText(state));
        _expiryValue.Text = manager.ExpiresAtUtc?.ToString("yyyy-MM-dd") ?? "—";
        _graceValue.Text = manager.GraceDaysRemaining.ToString();
        _idValue.Text = string.IsNullOrEmpty(manager.LicenseIdTruncated) ? "—" : manager.LicenseIdTruncated;
        _hint.Text = (state == LicenseState.Expired || state == LicenseState.Invalid)
            ? Localization.T("license.expired_hint")
            : "";
    }

    private void SetBanner(LicenseState state, string stateText)
    {
        _statusDot.Fill = DotColorFor(state);
        _statusTitle.Text = stateText;
        _statusDetail.Text = Localization.T("license.banner_detail_" + state.ToString().ToLowerInvariant());
    }

    private static Brush DotColorFor(LicenseState state)
    {
        switch (state)
        {
            case LicenseState.Active:
            case LicenseState.Trial:   return ActiveGreen;
            case LicenseState.Grace:   return GraceAmber;
            case LicenseState.Expired:
            case LicenseState.Invalid: return ExpiredRed;
            default:                   return ExpiredRed;
        }
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
