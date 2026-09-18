namespace PerfTest.Benchmarking;

public sealed class MixedTest : BenchmarkTestBase
{
    public override async Task ExecutePassAsync(
        Database database,
        CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(database);
        var cpfs = await GetRandomExistingCpfsAsync(3, cancellationToken);

        foreach (var cpf in cpfs)
        {
            var person = await repository.GetPersonByCpfAsync(cpf, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The seeded person with CPF '{cpf}' was not found.");
            await repository.GetLatestLimitsByPersonAsync(
                person.Id,
                5,
                cancellationToken);
            await repository.CreateLimitAsync(
                CreateRandomLimit(person.Id),
                cancellationToken);
        }
    }
}
