using System.Threading.Tasks;

namespace Configer.Services
{
    public class UvDependencyChecker : IDependencyChecker
    {
        public string Name => "UV 依赖";
        public string IconPath => "ms-appx:///Assets/UVLogo.png";

        private static readonly DependencyRequirement[] Requirements =
        {
            new("uv.exe", "uv.exe"),
            new("uv.toml", "uv.toml"),
            new("pyproject.toml", "pyproject.toml")
        };

        public Task<DependencyCheckResult> CheckAsync(string rootPath)
        {
            var res = DependencyCheckerHelper.CreateFileRequirementResult(Name, IconPath, rootPath, Requirements);
            return Task.FromResult(res);
        }
    }
}
