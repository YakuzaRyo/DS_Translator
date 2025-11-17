using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.System;

namespace Configer.Views
{
    public sealed partial class HelpView : UserControl
    {
    private bool _isLoaded;
    private string? _cachedReadmePath;

        public HelpView()
        {
            this.InitializeComponent();
            Loaded += HelpView_Loaded;
            SizeChanged += HelpView_SizeChanged;
        }

        public async Task EnsureReadmeLoadedAsync(bool forceRefresh = false)
        {
            if (_isLoaded && !forceRefresh)
            {
                return;
            }

            MarkdownHost.Text = "正在读取 README.md …";

            try
            {
                var readmePath = ResolveReadmePath();
                if (string.IsNullOrEmpty(readmePath))
                {
                    MarkdownHost.Text = "未在应用根目录找到 README.md 文件。";
                    _isLoaded = false;
                    return;
                }

                var content = await File.ReadAllTextAsync(readmePath);
                MarkdownHost.Text = string.IsNullOrWhiteSpace(content)
                    ? "README.md 内容为空。"
                    : content;

                _isLoaded = true;
                UpdateMarkdownFontSize(ActualWidth);
            }
            catch (Exception ex)
            {
                MarkdownHost.Text = $"加载 README.md 失败：{ex.Message}";
                _isLoaded = false;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await EnsureReadmeLoadedAsync(forceRefresh: true);
        }

        private void OpenReadmeButton_Click(object sender, RoutedEventArgs e)
        {
            var readmePath = ResolveReadmePath();
            if (!string.IsNullOrEmpty(readmePath) && File.Exists(readmePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = readmePath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else
            {
                MarkdownHost.Text = "未能定位 README.md，请确保它与程序位于同一目录或上级目录。";
            }
        }

        private string? ResolveReadmePath()
        {
            if (!string.IsNullOrEmpty(_cachedReadmePath) && File.Exists(_cachedReadmePath))
            {
                return _cachedReadmePath;
            }

            var current = AppContext.BaseDirectory;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(current); depth++)
            {
                var candidate = Path.GetFullPath(Path.Combine(current, "README.md"));
                if (File.Exists(candidate))
                {
                    _cachedReadmePath = candidate;
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            return null;
        }

        private void HelpView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMarkdownFontSize(e.NewSize.Width);
        }

        private void HelpView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMarkdownFontSize(ActualWidth);
        }

        private void UpdateMarkdownFontSize(double availableWidth)
        {
            if (availableWidth <= 0 || MarkdownHost is null)
            {
                return;
            }

            var baseSize = Math.Clamp(availableWidth / 42d, 14d, 24d);
            MarkdownHost.FontSize = baseSize;
            MarkdownHost.ParagraphLineHeight = (int)Math.Round(baseSize * 1.4);
            MarkdownHost.ParagraphMargin = new Thickness(0, baseSize * 0.4, 0, baseSize * 0.9);
            MarkdownHost.ListMargin = new Thickness(baseSize * 0.6, baseSize * 0.4, 0, baseSize * 0.8);
            MarkdownHost.ListGutterWidth = (int)Math.Round(baseSize * 1.2);
            MarkdownHost.ListBulletSpacing = baseSize * 0.35;

            MarkdownHost.Header1FontSize = baseSize + 16;
            MarkdownHost.Header2FontSize = baseSize + 10;
            MarkdownHost.Header3FontSize = baseSize + 6;
            MarkdownHost.Header4FontSize = baseSize + 4;
            MarkdownHost.Header5FontSize = baseSize + 2;
            MarkdownHost.Header6FontSize = baseSize + 1;

            MarkdownHost.Header1FontWeight = FontWeights.SemiBold;
            MarkdownHost.Header2FontWeight = FontWeights.SemiBold;
            MarkdownHost.Header3FontWeight = FontWeights.Medium;

            MarkdownHost.Header1Margin = new Thickness(0, baseSize * 1.3, 0, baseSize * 0.6);
            MarkdownHost.Header2Margin = new Thickness(0, baseSize, 0, baseSize * 0.5);
            MarkdownHost.Header3Margin = new Thickness(0, baseSize * 0.8, 0, baseSize * 0.4);
            MarkdownHost.Header4Margin = new Thickness(0, baseSize * 0.7, 0, baseSize * 0.35);

            MarkdownHost.QuoteMargin = new Thickness(0, baseSize * 0.5, 0, baseSize * 0.6);
            MarkdownHost.TableMargin = new Thickness(0, baseSize * 0.8, 0, baseSize * 0.8);
            MarkdownHost.CodeMargin = new Thickness(0, baseSize * 0.6, 0, baseSize * 0.8);
        }

        private async void MarkdownHost_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            if (Uri.TryCreate(e.Link, UriKind.Absolute, out var uri))
            {
                await Launcher.LaunchUriAsync(uri);
            }
        }
    }
}
