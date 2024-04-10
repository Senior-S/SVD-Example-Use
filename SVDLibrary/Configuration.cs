using Rocket.API;

namespace SeniorS.SVDLibrary;
public class Configuration : IRocketPluginConfiguration
{
    public void LoadDefaults()
    {
        apiURL = "";

        username = "ExampleUsername";
        key = "00000000000000000000000000000000";

        recordPermission = "ss.svd.record";
    }

    public string apiURL;

    public string username;
    public string key;

    public string recordPermission;
}