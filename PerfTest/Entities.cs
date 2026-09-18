namespace PerfTest;


public class Person
{
    public string Id { get; set; } = string.Empty; // can be int, string or guid, depending on the database
    public string Name { get; set; } = string.Empty;
    public string CPF { get; set; } = string.Empty; // unique
    public string AssociateCode { get; set; } = string.Empty; // random code
}

public class Limit
{
    public string Id { get; set; } = string.Empty; // can be int, string or guid, depending on the database
    public string PersonId { get; set; } = string.Empty; // fk with person
    public int OperationType { get; set; }
    public int Amount { get; set; }
    public DateTime Occurred { get; set; }
    public int OriginService { get; set; } // 0-120, indexed
    public string PolicyNumber { get; set; } = string.Empty;
}
