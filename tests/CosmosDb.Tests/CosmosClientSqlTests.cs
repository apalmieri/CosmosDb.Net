using CosmosDb.Tests.TestData;
using CosmosDb.Tests.TestData.Models;
using CosmosDB.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CosmosDb.Tests
{
    [TestClass]
#if !DEBUG
    [Ignore("Don't run on CI since it requries a conection to a CosmosDB. Update account name and key and run locally only")]
#endif
    public class CosmosClientSqlTests
    {
        private static string accountName = "5ff7e626-0ee0-4-231-b9ee";
        private static string accountKey = "KttOof6Ds6LWGyjEwEHa5k86Z9kyOnn6kihD9EZSLHu07anOSixxpsFnNYtV7knk6jpjWxy1HSRrNF4ol9I5mQ==";

        private static string accountEndpoint = $"https://{accountName}.documents.azure.com:443/";
        private static string connectionString = $"AccountEndpoint={accountEndpoint};AccountKey={accountKey};";
        private static string databaseId = "core";
        private static string containerId = "test1";

        private static string _partitionKeyPropertyName;

        private static string moviesTestDataPath = "TestData/Samples/movies_lite.csv";
        private static string castTestDataPath = "TestData/Samples/movies_cast_lite.csv";

        private static List<MovieFull> _moviesWithCast;
        private static List<MovieFull> _movies;
        private static List<MovieFullStream> _moviesStream;
        private static ICosmosClientSql _cosmosClient;

        [ClassInitialize]
        public static async Task Initialize(TestContext context)
        {
            var moviesCsv = Helpers.GetFromCsv<MovieCsv>(moviesTestDataPath);
            var castCsv = Helpers.GetFromCsv<CastCsv>(castTestDataPath);

            var cast = castCsv.GroupBy(k => k.MovieId).ToDictionary(k => k.Key, v => v.ToList());
            _moviesWithCast = moviesCsv.Select(m => MovieFull.GetMovieFull(m, cast.ContainsKey(m.TmdbId) ? cast[m.TmdbId] : new List<CastCsv>())).ToList();
            //Don't add Cast into the movie document - testing performance vs graph
            _movies = moviesCsv.Select(m => MovieFull.GetMovieFull(m, new List<CastCsv>())).ToList();
            _moviesStream = moviesCsv.Select(m => MovieFullStream.GetMovieFullStream(m, new List<CastCsv>())).ToList();

            Assert.AreEqual(4802, moviesCsv.Count());

            _cosmosClient = await CosmosClientSql.GetByConnectionString(connectionString, databaseId, containerId);
            var r = await _cosmosClient.Container.ReadContainerAsync();
            _partitionKeyPropertyName = r.Resource.PartitionKeyPath.Trim('/');
        }
        #region Initializers

        [TestMethod]
        public async Task GetClientWithAccountName()
        {
            var ccq = await CosmosClientSql.GetByAccountName(accountName, accountKey, databaseId, containerId);
            Assert.IsNotNull(ccq);

            var read = await ccq.ExecuteSQL<MovieFull>($"select * from c where c.Title = 'Avatar'");
            Assert.IsTrue(read.IsSuccessful);
        }

        [TestMethod]
        public async Task GetClientWithAccountEndpoint()
        {
            var ccq = await CosmosClientSql.GetByAccountName(accountEndpoint, accountKey, databaseId, containerId);
            Assert.IsNotNull(ccq);

            var read = await ccq.ExecuteSQL<MovieFull>($"select * from c where c.Title = 'Avatar'");
            Assert.IsTrue(read.IsSuccessful);
        }

        [TestMethod]
        public async Task GetClientWithConnectionString()
        {
            var ccq = await CosmosClientSql.GetByConnectionString(connectionString, databaseId, containerId);
            Assert.IsNotNull(ccq);

            var read = await ccq.ExecuteSQL<MovieFull>($"select * from c where c.Title = 'Avatar'");
            Assert.IsTrue(read.IsSuccessful);
        }


        [TestMethod]
        public async Task GetClientWithConnectionStringNoDatabaseNoContainerDatabaseTrhoughput()
        {
            var dbId = DateTime.Now.Ticks.ToString();
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId, "/pk") { DatabaseThrouhput = 1000 });

                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Database.DeleteAsync();
            }
        }

        [TestMethod]
        public async Task GetClientWithConnectionStringNoDatabaseNoContainerContainerTrhoughput()
        {
            var dbId = DateTime.Now.Ticks.ToString();
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId) { ContainerThroughput = 1000 });

                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Database.DeleteAsync();
            }
        }

        [TestMethod]
        public async Task GetClientWithConnectionStringNoDatabaseNoContainerDatabaseAndContainerTrhoughput()
        {
            var dbId = DateTime.Now.Ticks.ToString();
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId) { DatabaseThrouhput = 1000, ContainerThroughput = 1000 }); ; ;

                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Database.DeleteAsync();
            }
        }

        [TestMethod]
        public async Task GetClientWithConnectionStringNoDatabaseNoContainerNoThroughput()
        {
            var dbId = DateTime.Now.Ticks.ToString();
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                //Not providing thoughput defualts to 400RU container level
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId)); ; ;

                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Database.DeleteAsync();
            }
        }

        [TestMethod]
        public async Task GetClientWithConnectionStringNoContainerDatabaseTrhoughput()
        {
            var dbId = "core";
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId) { DatabaseThrouhput = 400 });

                //The database already exists, the new value we set should not have been applied.
                var database = await client.Database.ReadThroughputAsync();
                Assert.AreNotEqual(400, database.Value);


                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Container.DeleteContainerAsync();
            }
        }

        [TestMethod]
        public async Task GetClientWithConnectionStringNoContainerContainerTrhoughput()
        {
            var dbId = "core";
            var cId = "newContainer";
            ICosmosClientSql client = null;
            try
            {
                await Assert.ThrowsExceptionAsync<CosmosException>(() => CosmosClientSql.GetByConnectionString(connectionString, dbId, cId));
                client = await CosmosClientSql.GetByConnectionString(connectionString, dbId, cId, new CreateOptions(dbId, cId, "/pk") { DatabaseThrouhput = 400, ContainerThroughput = 400 });

                //The database already exists, the new value we set should not have been applied.
                var databaseRUs = await client.Database.ReadThroughputAsync();
                Assert.AreNotEqual(400, databaseRUs.Value);

                var container = await client.Container.ReadContainerAsync();
                Assert.AreEqual("/pk", container.Resource.PartitionKeyPath);

                var containerRUs = await client.Container.ReadThroughputAsync();
                Assert.AreEqual(400, containerRUs.Value);

                var read = await client.ExecuteSQL<int>($"SELECT VALUE COUNT(c) FROM c");
                Assert.IsTrue(read.IsSuccessful);
                Assert.AreEqual(0, read.Result.FirstOrDefault());
            }
            finally
            {
                await client?.Container.DeleteContainerAsync();
            }
        }

        #endregion

        [TestMethod]
        [Priority(1)]
        public async Task InsertCosmosDocument()
        {
            var movie = _movies.ElementAt(0);

            var insert = await _cosmosClient.InsertDocument(movie);
            Assert.IsTrue(insert.IsSuccessful);

            var read = await _cosmosClient.ReadDocument<MovieFull>(movie.TmdbId, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            var insert2 = await _cosmosClient.InsertDocument(movie);
            Assert.IsFalse(insert2.IsSuccessful, "Insert with same id should fail");

            Helpers.AssertMovieFullIsSame(movie, read.Result);
        }

        [TestMethod]
        [Priority(1)]
        public async Task InsertCosmosDocumentStream()
        {
            var movie = _moviesStream.ElementAt(0);

            var serializer = new JsonSerializer();
            using (var ms = new MemoryStream())
            {
                using (var sr = new StreamWriter(ms))
                {
                    serializer.Serialize(sr, movie);
                    sr.Flush();
                    ms.Position = 0;
                    var pk = movie.Title.ToString();

                    var insert = await _cosmosClient.InsertDocument(ms, new PartitionKey(pk));
                    Assert.IsTrue(insert.IsSuccessful);
                }
            }

            var read = await _cosmosClient.ReadDocument<MovieFullStream>(movie.id, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            Helpers.AssertMovieFullIsSame(movie, read.Result);
        }

        [TestMethod]
        [Priority(2)]
        public async Task UpsertCosmosDocument()
        {
            var movie = _movies.ElementAt(1);

            var upsert = await _cosmosClient.UpsertDocument(movie);
            Assert.IsTrue(upsert.IsSuccessful);

            var read = await _cosmosClient.ReadDocument<MovieFull>(movie.TmdbId, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            Helpers.AssertMovieFullIsSame(movie, read.Result);

            movie.Budget += 1;

            var upsert2 = await _cosmosClient.UpsertDocument(movie);
            Assert.IsTrue(upsert2.IsSuccessful);
            var read2 = await _cosmosClient.ReadDocument<MovieFull>(movie.TmdbId, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            Helpers.AssertMovieFullIsSame(movie, read2.Result);
        }

        [TestMethod]
        [Priority(1)]
        public async Task UpsertCosmosDocumentStream()
        {
            var movie = _moviesStream.ElementAt(1);

            var serializer = new JsonSerializer();
            using (var ms = new MemoryStream())
            {
                using (var sr = new StreamWriter(ms))
                {
                    serializer.Serialize(sr, movie);
                    sr.Flush();
                    ms.Position = 0;
                    var pk = movie.Title.ToString();

                    var insert = await _cosmosClient.UpsertDocument(ms, new PartitionKey(pk));
                    Assert.IsTrue(insert.IsSuccessful);
                }
            }

            var read = await _cosmosClient.ReadDocument<MovieFullStream>(movie.id, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            Helpers.AssertMovieFullIsSame(movie, read.Result);
        }

        [TestMethod]
        [Priority(3)]
        public async Task Insert201CosmosDocuments()
        {
            //201 items so we have 3 pages.
            var insert = await _cosmosClient.InsertDocuments(_movies.Skip(10).Take(201), (partial) => { Console.WriteLine($"inserted {partial.Count()} documents"); });

            var totalRu = insert.Sum(i => i.RequestCharge);
            var totalTime = insert.Sum(i => i.ExecutionTime.TotalSeconds);

            Assert.IsTrue(insert.All(i => i.IsSuccessful));
        }

        [TestMethod]
        [Priority(4)]
        public async Task Upsert201CosmosDocuments()
        {
            //201 items so we have 3 pages.
            var insert = await _cosmosClient.UpsertDocuments(_movies.Skip(10).Take(201), (partial) => { Console.WriteLine($"upserted {partial.Count()} documents"); });

            var totalRu = insert.Sum(i => i.RequestCharge);
            var totalTime = insert.Sum(i => i.ExecutionTime.TotalSeconds);

            Assert.IsTrue(insert.All(i => i.IsSuccessful));
        }

        [TestMethod]
        [Priority(4)]
        public async Task Upsert201CosmosDocumentsStream()
        {
            var serializer = new JsonSerializer();
            var streams = _moviesStream.Skip(10).Take(201).Select(_ => GetStreamAndPartitionKeyFromItem(_, "Title", serializer));

            //201 items so we have 3 pages.
            var insert = await _cosmosClient.UpsertDocuments(streams, (partial) => { Console.WriteLine($"upserted {partial.Count()} documents"); });

            var totalRu = insert.Sum(i => i.RequestCharge);
            var totalTime = insert.Sum(i => i.ExecutionTime.TotalSeconds);

            Assert.IsTrue(insert.All(i => i.IsSuccessful));

            foreach (var s in streams) s.stream.Dispose();
           
        }

        public (Stream stream, PartitionKey partitionKey) GetStreamAndPartitionKeyFromItem<T>(T item, string partitionKeyName = "PartitionKey", JsonSerializer serializer = null)
        {
            if (serializer == null) serializer = new JsonSerializer();
            var pkProp = typeof(T).GetProperty(partitionKeyName);
            var pk = new PartitionKey(pkProp.GetValue(item).ToString());
            var ms = new MemoryStream();
            var sr = new StreamWriter(ms);
            serializer.Serialize(sr, item);
            sr.Flush();
            ms.Position = 0;
            return (ms, pk);
        }

        [TestMethod]
        [Priority(10)]
        public async Task ReadDocument()
        {
            var movie = _movies.ElementAt(0);

            var read = await _cosmosClient.ReadDocument<MovieFull>(movie.TmdbId, movie.Title);
            Assert.IsTrue(read.IsSuccessful);

            Helpers.AssertMovieFullIsSame(movie, read.Result);
        }

        [TestMethod]
        [Priority(10)]
        public async Task ExecuteSql()
        {
            var movie = _movies.ElementAt(0);

            var read = await _cosmosClient.ExecuteSQL<MovieFull>($"select * from c where c.Title = '{movie.Title}'");

            Assert.IsTrue(read.IsSuccessful);
            Helpers.AssertMovieFullIsSame(movie, read.Result.FirstOrDefault());
        }

        [TestMethod]
        [Priority(10)]
        public async Task ExecuteSqlWithContinuation()
        {
            var query = $"select * from c order by c.Title";
            var readFirst = await _cosmosClient.ExecuteSQL<MovieFull>(query, true);
            Assert.IsTrue(readFirst.IsSuccessful);
            Assert.IsFalse(string.IsNullOrWhiteSpace(readFirst.ContinuationToken));

            var readnextPage = await _cosmosClient.ExecuteSQL<MovieFull>(query, true, readFirst.ContinuationToken);
            Assert.IsTrue(readnextPage.IsSuccessful);
            Assert.IsFalse(string.IsNullOrWhiteSpace(readnextPage.ContinuationToken));
        }

        [TestMethod]
        [Priority(10)]
        public async Task ExecuteSqlSpecificParameters()
        {
            var read = await _cosmosClient.ExecuteSQL<MovieFull>("select c.Title, c.Tagline, c.Overview from c");
            Assert.IsTrue(read.IsSuccessful);
        }


        [TestMethod]
        [Priority(10)]
        public async Task ExecuteSqlCustomReturn()
        {
            var read = await _cosmosClient.ExecuteSQL<object>("select c.Title, c.Tagline, c.Overview from c");
            Assert.IsTrue(read.IsSuccessful);
        }


        [TestMethod]
        [Priority(10)]
        public async Task ExecuteSqlValueReturn()
        {
            var read3 = await _cosmosClient.ExecuteSQL<string>("SELECT VALUE c.Title FROM c where c.label = 'Movie'");
            Assert.IsTrue(read3.IsSuccessful);
        }




        //[TestMethod]
        //[Priority(10)]
        public async Task Upsert5000CosmosDocuments()
        {
            var insert = await _cosmosClient.UpsertDocuments(_movies.Take(5000), (partial) => { Console.WriteLine($"upserted {partial.Count()} documents"); });

            var totalRu = insert.Sum(i => i.RequestCharge);
            var totalTime = insert.Sum(i => i.ExecutionTime.TotalSeconds);

            Assert.IsTrue(insert.All(i => i.IsSuccessful));
        }


        [TestMethod]
        [Priority(100)]
        public async Task TestIdInvalidIdCharacters()
        {
            //https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.documents.resource.id?redirectedfrom=MSDN&view=azure-dotnet#overloads
            var good = new TestModel { Id = "good-id", Pk = "good-partition" };
            var withSpace = new TestModel { Id = "id with space", Pk = "good-partition" };
            var withSlash = new TestModel { Id = "id-with-/", Pk = "good-partition" };
            var withBackslash = new TestModel { Id = "id-with-\\", Pk = "good-partition" };
            var withHash = new TestModel { Id = "id-with-#", Pk = "good-partition" };
            var withDollar = new TestModel { Id = "id-with-$", Pk = "good-partition" };

            var insertGood = await _cosmosClient.UpsertDocument(good);
            var insertwithSpace = await _cosmosClient.UpsertDocument(withSpace);
            var insertwithSlash = await _cosmosClient.UpsertDocument(withSlash);
            var insertwithBackslash = await _cosmosClient.UpsertDocument(withBackslash);
            var insertwithHash = await _cosmosClient.UpsertDocument(withHash);
            var insertwithDollar = await _cosmosClient.UpsertDocument(withDollar);

            Assert.IsTrue(insertGood.IsSuccessful);
            Assert.IsTrue(insertwithSpace.IsSuccessful);
            Assert.IsTrue(insertwithSlash.IsSuccessful);
            Assert.IsTrue(insertwithBackslash.IsSuccessful);
            Assert.IsTrue(insertwithHash.IsSuccessful);
            Assert.IsTrue(insertwithDollar.IsSuccessful);
        }


        [TestMethod]
        [Priority(100)]
        public async Task TestIdInvalidPkCharacters()
        {
            //https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.documents.resource.id?redirectedfrom=MSDN&view=azure-dotnet#overloads
            var good = new TestModel { Id = "good-id", Pk = "good-partition" };
            var withSpace = new TestModel { Id = "good-id", Pk = "partition with space" };
            var withSlash = new TestModel { Id = "good-id", Pk = "good-partition-with-/" };
            var withBackslash = new TestModel { Id = "good-id", Pk = "good-partitionwith-\\" };
            var withHash = new TestModel { Id = "good-id", Pk = "good-partition-with-#" };
            var withDollar = new TestModel { Id = "good-id", Pk = "good-partition-with-$" };

            var insertGood = await _cosmosClient.UpsertDocument(good);
            var insertwithSpace = await _cosmosClient.UpsertDocument(withSpace);
            var insertwithSlash = await _cosmosClient.UpsertDocument(withSlash);
            var insertwithBackslash = await _cosmosClient.UpsertDocument(withBackslash);
            var insertwithHash = await _cosmosClient.UpsertDocument(withHash);
            var insertwithDollar = await _cosmosClient.UpsertDocument(withDollar);

            Assert.IsTrue(insertGood.IsSuccessful);
            Assert.IsTrue(insertwithSpace.IsSuccessful);
            Assert.IsTrue(insertwithSlash.IsSuccessful);
            Assert.IsTrue(insertwithBackslash.IsSuccessful);
            Assert.IsTrue(insertwithHash.IsSuccessful);
            Assert.IsTrue(insertwithDollar.IsSuccessful);
        }
    }
}
