using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DataObjects;
using YamlDotNet.Serialization;
using System.IO;

// Message Types
using RosMessageTypes.Gazebo;
using RosMessageTypes.Unity;
using UnityEngine.InputSystem;

public class RobotController : MonoBehaviour
{
    private CommandLineParser commandLineArgs;
    private string simNamespace;
    private readonly string CollisionSensorName = "CollisionSensor";
    private readonly string PedSafeDistSensorName = "PedSafeDistSensor";
    private readonly string ObsSafeDistSensorName = "ObsSafeDistSensor";

    void Start()
    {
        commandLineArgs = gameObject.AddComponent<CommandLineParser>();
        commandLineArgs.Initialize();

        simNamespace = commandLineArgs.sim_namespace != null ? "/" + commandLineArgs.sim_namespace : "";
    }

    private string GetConfigFileContent(string relativeArenaSimSetupPath)
    {
        // Construct the full path robot yaml path
        // Take command line arg if executable build is running
        string arenaSimSetupPath = commandLineArgs.arena_sim_setup_path;
        // Use relative path if running in Editor
        arenaSimSetupPath ??= Path.Combine(Application.dataPath, "../../simulation-setup");
        string yamlPath = Path.Combine(arenaSimSetupPath, "entities", "robots", robotName, robotName + ".model.yaml");
        string configPath = Path.Combine(arenaSimSetupPath, relativeArenaSimSetupPath);

        // Check if the file exists
        if (!File.Exists(configPath))
        {
            Debug.LogError("Config file could not be found at: " + configPath);
            return null;
        }

        // Read the config file
        return File.ReadAllText(configPath);
    }

    private RobotConfig LoadRobotModelYaml(string robotName)
    {
        // Get yaml file content
        string relativeYamlPath = Path.Combine("entities", "robots", robotName, robotName + ".model.yaml");
        string yamlContent = GetConfigFileContent(relativeYamlPath);
        if (yamlContent == null)
        {
            Debug.LogError("Robot model yaml file could not be found at: " + relativeYamlPath);
            return null;
        }

        // Initialize the deserializer
        var deserializer = new DeserializerBuilder().Build();

        // Deserialize the YAML content into a dynamic object
        RobotConfig config = deserializer.Deserialize<RobotConfig>(yamlContent);

        return config;
    }

    private RobotUnityConfig LoadRobotUnityParamsYaml(string robotName)
    {
        // Get yaml file content
        string relativeYamlPath = Path.Combine("entities", "robot", robotName, "unity", "unity_params.yaml");
        string yamlContent = GetConfigFileContent(relativeYamlPath);
        if (yamlContent == null)
        {
            Debug.LogError("Unity specific params yaml file could not be found at: " + relativeYamlPath);
            return null;
        }

        // Initialize the deserializer
        var deserializer = new DeserializerBuilder().Build();

        // Deserialize the YAML content into a dynamic object
        RobotUnityConfig config = deserializer.Deserialize<RobotUnityConfig>(yamlContent);
        
        return config;
    }

    private static Dictionary<string, object> GetPluginDict(RobotConfig config, string pluginTypeName)
    {
        Dictionary<string, object> targetDict = null;

        // Find Laser Scan configuration in list of plugins
        foreach (Dictionary<string, object> dict in config.plugins)
        {
            // check if type is actually laser scan
            if (dict.TryGetValue("type", out object value))
            {
                if (value is string strValue && strValue.Equals(pluginTypeName))
                {
                    targetDict = dict;
                    break;
                }
            }
        }

        return targetDict;
    }

    private static GameObject GetLaserLinkJoint(GameObject robot, Dictionary<string, object> laserDict)
    {
        // check if laser configuration has fram/joint specified
        if (!laserDict.TryGetValue("frame", out object frameName))
        {
            Debug.LogError("Robot Model Config for Laser Scan has no frame specified!");
            return null;
        }

        // get laser scan frame joint game object
        string laserJointName = frameName as string;
        Transform laserScanFrameTf = Utils.FindChildGameObject(robot.transform, laserJointName);
        if (laserScanFrameTf == null)
        {
            Debug.LogError("Robot has no joint game object as specified in Model Config for laser scan!");
            return null;
        }

        return laserScanFrameTf.gameObject;
    }

    private void HandleLaserScan(GameObject robot, RobotConfig config, string robotNamespace)
    {
        if (config == null)
        {
            Debug.LogError("Given robot config was null (probably incorrect config path). Robot will be spawned without scan");
            return;
        }

        // get configuration of laser scan from robot configuration
        Dictionary<string, object> laserDict = GetPluginDict(config, "Laser");
        if (laserDict == null)
        {
            Debug.LogError("Robot Model Configuration has no Laser plugin. Robot will be spawned without scan");
            return;
        }

        // find frame join game object for laser scan
        GameObject laserLinkJoint = GetLaserLinkJoint(robot, laserDict);
        if (laserLinkJoint == null)
        {
            Debug.LogError("No laser link joint was found. Robot will be spawned without scan.");
            return;
        }

        // attach LaserScanSensor
        LaserScanSensor laserScan = laserLinkJoint.AddComponent(typeof(LaserScanSensor)) as LaserScanSensor;
        laserScan.topicNamespace = simNamespace + "/" + robotNamespace;
        laserScan.frameId = robotNamespace + "/" + laserLinkJoint.name;

        // TODO: this is missing the necessary configuration of all parameters according to the laser scan config
        laserScan.ConfigureScan(laserDict);
    }

    private void HandleCollider(GameObject robot, RobotUnityConfig config, string robotNamespace)
    {
        if (config == null)
        {
            Debug.LogError("Given Unity-specific config was null (make sure it exists for robot model)");
            return;
        }
        if (!config.components.TryGetValue("collider", out Dictionary<string, object> colliderDict))
        {
            Debug.LogWarning("Unity-specific config does not specify collider component.");
            return;
        }

        GameObject collisionSensorObject = new(CollisionSensorName);

        // attach collider 
        CapsuleCollider collider = collisionSensorObject.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        
        // attach collider sensor
        CollisionSensor collisionSensor = collisionSensorObject.AddComponent<CollisionSensor>();
        collisionSensor.colliderComponent = collider;
        collisionSensor.topicNamespace = simNamespace + "/" + robotNamespace;
        collisionSensor.ConfigureCollider(colliderDict);

        // center child collision sensor
        collisionSensorObject.transform.SetParent(robot.transform);
        collisionSensorObject.transform.SetPositionAndRotation(
            robot.transform.position, 
            robot.transform.rotation
        );
    }

    public GameObject SpawnRobot(SpawnModelRequest request)
    {
        // process spawn request for robot
        GameObject entity = Utils.CreateGameObjectFromUrdfFile(
            request.model_xml,
            request.robot_namespace,
            disableJoints: true,
            disableScripts: true,
            parent: null
        );

        // get base link which is the second child after Plugins
        Transform baseLinkTf = entity.transform.GetChild(1);

        // Set up TF by adding TF publisher to the base_footprint game object
        baseLinkTf.gameObject.AddComponent(typeof(ROSTransformTreePublisher));

        // Set up Drive
        Drive drive = entity.AddComponent(typeof(Drive)) as Drive;
        drive.topicNamespace = simNamespace + "/" + request.robot_namespace;

        // Set up Odom publishing (this relies on the Drive -> must be added after Drive)
        OdomPublisher odom = baseLinkTf.gameObject.AddComponent(typeof(OdomPublisher)) as OdomPublisher;
        odom.topicNamespace = simNamespace + "/" + request.robot_namespace;
        odom.robotName = request.robot_namespace;

        // transport to starting pose
        Utils.SetPose(entity, request.initial_pose);

        // add gravity to robot
        Rigidbody rb = entity.AddComponent(typeof(Rigidbody)) as Rigidbody;
        rb.useGravity = true;

        // try to attach laser scan sensor
        RobotConfig config = LoadRobotModelYaml(request.model_name);
        HandleLaserScan(entity, config, request.robot_namespace);

        // try to attach collider sensor
        RobotUnityConfig unityConfig = LoadRobotUnityParamsYaml(request.model_name);
        HandleCollider(entity, unityConfig, request.robot_namespace);

        return entity;
    }

    public bool AttachSafeDistSensor(GameObject robot, AttachSafeDistSensorRequest request)
    {
        string sensorName = "";
        if (request.ped_safe_dist)
            sensorName = PedSafeDistSensorName;
        else if (request.obs_safe_dist)
            sensorName = ObsSafeDistSensorName;
        else
            return false;  // invalid request;

        // Get main collision sensor for configurations
        GameObject collisionSensorObject = robot.transform.Find(CollisionSensorName).gameObject;
        CollisionSensor collisionSensor = collisionSensorObject.GetComponent<CollisionSensor>();
        if (collisionSensor == null)
            return false;

        // Replace old safe dist sensor if existent
        Transform old = robot.transform.Find(sensorName);
        if (old != null)
            Destroy(old.gameObject);

        // Configure Collider
        GameObject safeDistSensorObject = new(sensorName);
        CapsuleCollider collider = safeDistSensorObject.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        // Use main collider configurations
        collider.height = collisionSensor.colliderComponent.height;
        collider.center = collisionSensor.colliderComponent.center;
        collider.radius = collisionSensor.colliderComponent.radius + (float)request.safe_dist;

        // Configure new safe dist sensor
        CollisionSensor safeDistSensor = safeDistSensorObject.AddComponent<CollisionSensor>();
        safeDistSensor.colliderComponent = collider;
        safeDistSensor.topicNamespace = simNamespace + "/" + request.robot_name;
        safeDistSensor.collsionTopicName = request.safe_dist_topic;
        safeDistSensor.detectPed = request.ped_safe_dist;
        safeDistSensor.detectObs = request.obs_safe_dist;
        

        safeDistSensorObject.transform.SetParent(robot.transform);
        safeDistSensorObject.transform.SetPositionAndRotation(
            robot.transform.position, 
            robot.transform.rotation
        );
        return true;
    }
}
