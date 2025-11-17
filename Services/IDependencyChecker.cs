using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Configer.Services
{
    public interface IDependencyChecker
    {
        string Name { get; }
        Task<DependencyCheckResult> CheckAsync(string rootPath);
    }

    public class DependencyCheckResult : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private bool _isSuccess;
        private string _resultText = string.Empty;
        private bool _isExpanded;
        private string? _iconPath;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool IsSuccess
        {
            get => _isSuccess;
            set => SetProperty(ref _isSuccess, value);
        }

        public string ResultText
        {
            get => _resultText;
            set => SetProperty(ref _resultText, value);
        }

        // UI expansion state for the details panel
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        // Optional icon URI (e.g. "ms-appx:///Assets/UVLogo.png")
        public string? IconPath
        {
            get => _iconPath;
            set => SetProperty(ref _iconPath, value);
        }

        // Individual items inside this check (e.g., files to verify)
        public ObservableCollection<DependencyCheckItem> Items { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class DependencyCheckItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
    }

    public record DependencyRequirement(string Name, string RelativePath);

    public static class DependencyCheckerHelper
    {
        public static DependencyCheckResult CreateFileRequirementResult(string checkerName, string? iconPath, string rootPath, params DependencyRequirement[] requirements)
        {
            var result = new DependencyCheckResult
            {
                Name = checkerName,
                IconPath = iconPath
            };

            ApplyFileRequirements(result, rootPath, requirements);
            return result;
        }

        public static void ApplyFileRequirements(DependencyCheckResult result, string rootPath, params DependencyRequirement[] requirements)
        {
            result.Items.Clear();
            var missingNames = new List<string>();
            var statusLines = new List<string>();

            var maxNameLength = 0;
            foreach (var requirement in requirements)
            {
                if (requirement.Name.Length > maxNameLength)
                {
                    maxNameLength = requirement.Name.Length;
                }
            }

            foreach (var requirement in requirements)
            {
                var fullPath = Path.Combine(rootPath, requirement.RelativePath);
                var success = File.Exists(fullPath);
                result.Items.Add(new DependencyCheckItem { Name = requirement.Name, IsSuccess = success });
                var paddedName = requirement.Name.PadRight(maxNameLength);
                var statusText = success ? "[DONE]" : "[MISSING]";
                statusLines.Add($"{paddedName}\t{statusText}");

                if (!success)
                {
                    missingNames.Add(requirement.Name);
                }
            }

            result.IsSuccess = missingNames.Count == 0;
            result.ResultText = statusLines.Count == 0
                ? "暂无依赖"
                : $"{string.Join('\n', statusLines)}";
        }
    }
}
