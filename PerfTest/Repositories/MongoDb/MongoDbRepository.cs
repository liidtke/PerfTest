using MongoDB.Driver;

namespace PerfTest.Repositories.MongoDb;

public sealed class MongoDbRepository : IRepository
{
    private readonly IMongoCollection<Person> _persons;
    private readonly IMongoCollection<Limit> _limits;

    public MongoDbRepository(
        IMongoDatabase database,
        string personsCollection = "persons",
        string limitsCollection = "limits")
    {
        _persons = database.GetCollection<Person>(personsCollection);
        _limits = database.GetCollection<Limit>(limitsCollection);
    }

    public async Task<Person?> GetPersonAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        await _persons.Find(person => person.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Person?> GetPersonByCpfAsync(
        string cpf,
        CancellationToken cancellationToken = default) =>
        await _persons.Find(person => person.CPF == cpf)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAsync(
        string personId,
        CancellationToken cancellationToken = default) =>
        await _limits.Find(limit => limit.PersonId == personId)
            .SortBy(limit => limit.Occurred)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Limit>> GetLatestLimitsByPersonAsync(
        string personId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return await _limits.Find(limit => limit.PersonId == personId)
            .SortByDescending(limit => limit.Occurred)
            .Limit(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Limit>> GetLimitsByPersonAndDateAsync(
        string personId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<Limit>.Filter.And(
            Builders<Limit>.Filter.Eq(limit => limit.PersonId, personId),
            Builders<Limit>.Filter.Gte(limit => limit.Occurred, from),
            Builders<Limit>.Filter.Lte(limit => limit.Occurred, to));

        return await _limits.Find(filter)
            .SortBy(limit => limit.Occurred)
            .ToListAsync(cancellationToken);
    }

    public async Task<Limit?> GetLimitAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        await _limits.Find(limit => limit.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task CreatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default) =>
        _persons.InsertOneAsync(person, cancellationToken: cancellationToken);

    public async Task<bool> UpdatePersonAsync(
        Person person,
        CancellationToken cancellationToken = default)
    {
        var result = await _persons.ReplaceOneAsync(
            existing => existing.Id == person.Id,
            person,
            cancellationToken: cancellationToken);
        return result.MatchedCount > 0;
    }

    public Task CreateLimitAsync(
        Limit limit,
        CancellationToken cancellationToken = default) =>
        _limits.InsertOneAsync(limit, cancellationToken: cancellationToken);
}
