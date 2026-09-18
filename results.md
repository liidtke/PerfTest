6 minutes 
mixed 
8 strength

notas: container do mariadb usa mais memória que o do postgres
added pre warmup to postgres

5M Limits

╭─────┬──────────────────┬───────┬───────────┬──────────┬─────────┬────────┬──────────────┬──────────────┬──────────────╮
│ Run │ Completed        │ Type  │ Database  │ Strength │ Passes  │ Failed │ Average      │ P95          │ Memory       │
├─────┼──────────────────┼───────┼───────────┼──────────┼─────────┼────────┼──────────────┼──────────────┼──────────────┤
│ #11 │ 18/09/2026 16:47 │ Mixed │ ScyllaDb  │ 4        │ 358.660 │ 0      │ 15,612 ms    │ 21,431 ms    │ 3.646,51 MB  │
│ #10 │ 18/09/2026 16:44 │ Mixed │ Cassandra │ 4        │ 301.100 │ 0      │ 16,692 ms    │ 24,656 ms    │ 14.687,22 MB │
│ #9  │ 18/09/2026 16:40 │ Mixed │ MongoDb   │ 4        │ 328.500 │ 0      │ 12,495 ms    │ 19,172 ms    │ 1.204,69 MB  │
│ #8  │ 18/09/2026 16:37 │ Mixed │ Postgres  │ 4        │ 251.060 │ 0      │ 20,843 ms    │ 45,499 ms    │ 1.721,75 MB  │
│ #7  │ 18/09/2026 16:34 │ Mixed │ MariaDb   │ 4        │ 261.780 │ 0      │ 25,492 ms    │ 52,637 ms    │ 7.400,86 MB  │
│ #6  │ 18/09/2026 16:29 │ Mixed │ Postgres  │ 4        │ 192.060 │ 1      │ 18,083 ms    │ 25,548 ms    │ 1.531,29 MB  │




20M Limits
