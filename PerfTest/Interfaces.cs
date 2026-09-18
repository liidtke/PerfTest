namespace PerfTest;

public interface IRepository
{
    // Get a person by id.
    Task<Person?> GetPersonAsync(string id, CancellationToken cancellationToken = default);

    // Get a person by CPF.
    Task<Person?> GetPersonByCpfAsync(string cpf, CancellationToken cancellationToken = default);

    // Get all limits for a person.
    Task<IReadOnlyList<Limit>> GetLimitsByPersonAsync(
        string personId,
        CancellationToken cancellationToken = default);

    // Get the latest limits for a person.
    Task<IReadOnlyList<Limit>> GetLatestLimitsByPersonAsync(
        string personId,
        int count,
        CancellationToken cancellationToken = default);

    // Get all limits for a person in the inclusive date range.
    Task<IReadOnlyList<Limit>> GetLimitsByPersonAndDateAsync(
        string personId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    // Get a limit by id.
    Task<Limit?> GetLimitAsync(string id, CancellationToken cancellationToken = default);

    // Create a person.
    Task CreatePersonAsync(Person person, CancellationToken cancellationToken = default);

    // Update an existing person.
    Task<bool> UpdatePersonAsync(Person person, CancellationToken cancellationToken = default);

    // Create a limit.
    Task CreateLimitAsync(Limit limit, CancellationToken cancellationToken = default);
}