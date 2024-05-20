//Do not edit! This file was generated by Unity-ROS MessageGeneration.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.Pedsim
{
    [Serializable]
    public class RobotStateMsg : Message
    {
        public const string k_RosMessageName = "pedsim_msgs/RobotState";
        public override string RosMessageName => k_RosMessageName;

        public string name;
        public Geometry.PoseMsg pose;
        public Geometry.TwistMsg twist;

        public RobotStateMsg()
        {
            this.name = "";
            this.pose = new Geometry.PoseMsg();
            this.twist = new Geometry.TwistMsg();
        }

        public RobotStateMsg(string name, Geometry.PoseMsg pose, Geometry.TwistMsg twist)
        {
            this.name = name;
            this.pose = pose;
            this.twist = twist;
        }

        public static RobotStateMsg Deserialize(MessageDeserializer deserializer) => new RobotStateMsg(deserializer);

        private RobotStateMsg(MessageDeserializer deserializer)
        {
            deserializer.Read(out this.name);
            this.pose = Geometry.PoseMsg.Deserialize(deserializer);
            this.twist = Geometry.TwistMsg.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(this.name);
            serializer.Write(this.pose);
            serializer.Write(this.twist);
        }

        public override string ToString()
        {
            return "RobotStateMsg: " +
            "\nname: " + name.ToString() +
            "\npose: " + pose.ToString() +
            "\ntwist: " + twist.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register()
        {
            MessageRegistry.Register(k_RosMessageName, Deserialize);
        }
    }
}
