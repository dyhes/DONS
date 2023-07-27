using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Samples.DONSSystem
{
    public enum NodeType
    {
        HOST,
        SWITCH
    }
    public enum OSPFPacketType
    {
        HELLO,
        HELLORES,
        LSA
    }

    public struct LSAIdentifier : IBufferElementData
    {
        public int identifier;
    }

    public struct TopoEntry : IBufferElementData
    {
        public int identifier;
        public int vertexl_id;
        public int vertexg_id;
        public bool isValid;
    }

    public struct ReceivedPacketEntry : IBufferElementData
        {
        public OSPFPacket ospfPacket;
        }
    public struct OSPFPacket : IComponentData
    {
        public OSPFPacketType ospfPacketType;
        public int dest_id;
        public int source_id;
        public int transitting_frame;
        public bool ack;
        public TopoEntry topoEntry;
    }
    public struct SwitchData : IComponentData
    {
        public int switch_id;
        public int host_node;
        public int switch_node;
        public int fattree_K;
        public bool isWorking;
    }
    public struct RoutingEntry : IBufferElementData
    {
        public int dest_id;
        public int next_hop;
        public int distance;
    }
    public enum OSPFStatus
    {
        DOWN,
        TWO_WAY,
    }
    public struct AdjacencyEntry : IBufferElementData
    {
        public int node_id;
        public NodeType type;
        public OSPFStatus ospfStatus;
    }
}
