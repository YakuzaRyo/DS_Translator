namespace Configer.Models
{
    public class SrtFileItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Status { get; set; } = string.Empty;
    }
}
