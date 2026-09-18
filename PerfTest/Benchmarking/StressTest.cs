namespace PerfTest.Benchmarking;

public sealed class StressTest : BenchmarkTestBase
{
    public override async Task ExecutePassAsync(
        Database database,
        CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(database);
        var existingCpfs = await GetRandomExistingCpfsAsync(5, cancellationToken);
        var generatedPeople = Enumerable.Range(0, 5)
            .Select(_ => CreateRandomPerson())
            .ToArray();

        foreach (var cpf in existingCpfs)
        {
            var person = await repository.GetPersonByCpfAsync(cpf, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The seeded person with CPF '{cpf}' was not found.");
            await repository.GetLimitsByPersonAsync(person.Id, cancellationToken);
            await InsertLimitsAsync(repository, person.Id, cancellationToken);
        }

        foreach (var generatedPerson in generatedPeople)
        {
            var person = await repository.GetPersonByCpfAsync(
                generatedPerson.CPF,
                cancellationToken);
            if (person is null)
            {
                await repository.CreatePersonAsync(generatedPerson, cancellationToken);
                person = generatedPerson;
            }
            else
            {
                await repository.GetLimitsByPersonAsync(person.Id, cancellationToken);
            }

            await InsertLimitsAsync(repository, person.Id, cancellationToken);
        }
    }

    private static async Task InsertLimitsAsync(
        IRepository repository,
        string personId,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < 3; index++)
        {
            await repository.CreateLimitAsync(
                CreateRandomLimit(personId),
                cancellationToken);
        }
    }
}
