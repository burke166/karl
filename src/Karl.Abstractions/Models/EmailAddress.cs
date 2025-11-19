namespace Karl.Models;

public sealed class EmailAddress
{
    public string Address { get; }
    public string? Name { get; }

    public EmailAddress(string address, string? name = null)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Name = name;
    }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name)
            ? Address
            : $"{Name} <{Address}>";
}
