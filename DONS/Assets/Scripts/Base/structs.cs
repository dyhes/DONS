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

    public enum OSPFStatus
    {
        DOWN,
        INIT,
        TWO_WAY,
        EXCHANGE,
        FULL
    }

    public struct OSPFTransmitterData : IComponentData
    {
        public int tranmittingFrame;
    } 
    public struct LSAPacket : IBufferElementData
    {
        public int dest_id;
        public int source_id;
        public int transfering_frame;
    }
    public struct RoutingEntry : IBufferElementData
    {
        public int dest_id;
        public int next_hop;
        public int distance;
        public bool isExpired;
    }
    public struct AdjacencyEntry : IBufferElementData
    {
        public int node_id;
        public NodeType type;
        public OSPFStatus ospfStatus;
    }


    // inherited code
    public struct NodeEntity : IComponentData
    {
        public int node_id;
    }
    public struct SwitchData : IComponentData
    {
        public int switch_id;
        public int host_node;
        public int fattree_K;
        public bool isUpdating;
    }

    public struct QueueEntry : IBufferElementData
    {
        public int node_id;
    }
    public struct NodeEntry : IBufferElementData
    {
        public int dis;
        public int vis;
    }
    public struct Array2D : IBufferElementData
    {
        public int next_id;
    }
    public struct AdjacencyListEntry : IBufferElementData
    {
        public int next_id;
    }
    public struct EgressPortEntry : IBufferElementData
    {
        public int EgressPort_id;
        public Entity peer;
    }

    public struct BuildTopoOverFlag : IComponentData
    {

    }
}
