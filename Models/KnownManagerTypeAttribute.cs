using System.ComponentModel.DataAnnotations;

namespace AxiGitAPI.Models
{
    // Accepts only the known Manager Types (Product, ChangeRequest, Plugin),
    // compared case-insensitively. Blank values are left to [Required].
    [AttributeUsage(AttributeTargets.Property)]
    public class KnownManagerTypeAttribute : ValidationAttribute
    {
        public static readonly string[] ManagerTypes =
        {
            "Product",
            "ChangeRequest",
            "Plugin"
        };

        public KnownManagerTypeAttribute()
            : base($"ManagerType must be one of: {string.Join(", ", ManagerTypes)}.")
        {
        }

        public override bool IsValid(object? value)
        {
            if (value is not string managerType ||
                string.IsNullOrWhiteSpace(managerType))
            {
                return true;
            }

            return ManagerTypes.Contains(
                managerType.Trim(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
