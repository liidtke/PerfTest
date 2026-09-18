using MongoDB.Driver;
using MongoDB.Bson;
using PerfTest.Data;

namespace PerfTest.Seeders;

public sealed class MongoDbSeeder(IMongoDatabase database) : IDatabaseSeeder
{
    public string DatabaseName => "mongodb";

    public async Task SeedAsync(
        SqliteDatasetReader dataset,
        IProgress<SeedProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await DropIfExistsAsync("persons", cancellationToken);
        await DropIfExistsAsync("limits", cancellationToken);
        var persons = database.GetCollection<Person>("persons");
        var limits = database.GetCollection<Limit>("limits");

        long inserted = 0;
        await foreach (var batch in dataset.ReadPersonsAsync(5_000, cancellationToken))
        {
            await persons.InsertManyAsync(
                batch,
                new InsertManyOptions { IsOrdered = false },
                cancellationToken);
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "persons",
                inserted,
                SqliteDatasetGenerator.PersonCount));
        }

        inserted = 0;
        await foreach (var batch in dataset.ReadLimitsAsync(5_000, cancellationToken))
        {
            await limits.InsertManyAsync(
                batch,
                new InsertManyOptions { IsOrdered = false },
                cancellationToken);
            inserted += batch.Count;
            progress?.Report(new SeedProgress(
                DatabaseName,
                "limits",
                inserted,
                SqliteDatasetGenerator.LimitCount));
        }

        await persons.Indexes.CreateOneAsync(
            new CreateIndexModel<Person>(
                Builders<Person>.IndexKeys.Ascending(person => person.CPF),
                new CreateIndexOptions { Unique = true, Name = "ux_persons_cpf" }),
            cancellationToken: cancellationToken);

        await limits.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Limit>(
                    Builders<Limit>.IndexKeys
                        .Ascending(limit => limit.PersonId)
                        .Ascending(limit => limit.Occurred),
                    new CreateIndexOptions { Name = "ix_limits_person_occurred" }),
                new CreateIndexModel<Limit>(
                    Builders<Limit>.IndexKeys.Ascending(limit => limit.OriginService),
                    new CreateIndexOptions { Name = "ix_limits_origin_service" })
            ],
            cancellationToken);
    }

    private async Task DropIfExistsAsync(
        string collectionName,
        CancellationToken cancellationToken)
    {
        using var cursor = await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", collectionName)
            },
            cancellationToken);

        if (await cursor.AnyAsync(cancellationToken))
            await database.DropCollectionAsync(collectionName, cancellationToken);
    }
}
