using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Net.Http;

string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing AZURE_STORAGE_CONNECTION_STRING environment variable.");

string sqlConnectionString = Environment.GetEnvironmentVariable("AZURE_SQL_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing AZURE_SQL_CONNECTION_STRING environment variable.");

string containerName = "raw-data";
string locationName = "Las Vegas";
double latitude = 36.17;
double longitude = -115.14;

string apiUrl =
    $"https://api.open-meteo.com/v1/forecast" +
    $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
    $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
    $"&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m,precipitation" +
    $"&temperature_unit=fahrenheit" +
    $"&wind_speed_unit=mph" +
    $"&precipitation_unit=inch" +
    $"&forecast_days=1" +
    $"&timezone=America%2FLos_Angeles";

using HttpClient httpClient = new HttpClient();

Console.WriteLine("Calling Open-Meteo API...");
string jsonResponse = await httpClient.GetStringAsync(apiUrl);

DateTime nowUtc = DateTime.UtcNow;

string blobName =
    $"weather/open-meteo/year={nowUtc:yyyy}/month={nowUtc:MM}/day={nowUtc:dd}/weather_las_vegas_{nowUtc:yyyyMMdd_HHmmss}.json";

BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

await containerClient.CreateIfNotExistsAsync();

BlobClient blobClient = containerClient.GetBlobClient(blobName);

Console.WriteLine($"Uploading raw JSON to Blob Storage: {blobName}");
await blobClient.UploadAsync(BinaryData.FromString(jsonResponse), overwrite: true);

Console.WriteLine("Blob upload complete.");

Console.WriteLine("Connecting to Azure SQL...");
using SqlConnection sqlConnection = new SqlConnection(sqlConnectionString);

await sqlConnection.OpenAsync();

Console.WriteLine("Inserting raw JSON into dbo.weather_raw_json...");

string insertSql = @"
INSERT INTO dbo.weather_raw_json (
    source_name,
    blob_file_name,
    raw_json
)
VALUES (
    @source_name,
    @blob_file_name,
    @raw_json
);";

using SqlCommand insertCommand = new SqlCommand(insertSql, sqlConnection);

insertCommand.Parameters.Add("@source_name", SqlDbType.VarChar, 100).Value = "Open-Meteo";
insertCommand.Parameters.Add("@blob_file_name", SqlDbType.VarChar, 500).Value = blobName;
insertCommand.Parameters.Add("@raw_json", SqlDbType.NVarChar, -1).Value = jsonResponse;

await insertCommand.ExecuteNonQueryAsync();

Console.WriteLine("Raw JSON inserted into SQL.");

Console.WriteLine("Executing stored procedure dbo.LoadWeatherHourlyFromRawJson...");

using SqlCommand procedureCommand = new SqlCommand(
    "dbo.LoadWeatherHourlyFromRawJson",
    sqlConnection
);

procedureCommand.CommandType = CommandType.StoredProcedure;
procedureCommand.CommandTimeout = 60;

await procedureCommand.ExecuteNonQueryAsync();

Console.WriteLine("Stored procedure complete. Weather data loaded into dbo.weather_hourly.");
Console.WriteLine("Pipeline branch complete.");