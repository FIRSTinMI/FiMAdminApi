namespace FiMAdminApi.Clients.Exceptions;

public class MissingDataException(string dataPath) : Exception
{
    public string DataPath { get; set; } = dataPath;

    public override string ToString()
    {
        return $"Expected data is missing: {dataPath}";
    }
}