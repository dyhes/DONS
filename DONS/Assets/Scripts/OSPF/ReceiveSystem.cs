using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Samples.DONSSystem
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(TransmitSystem))]
    public partial class ReceiveSystem : SystemBase
    {
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;
        protected override void OnCreate()
        {
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }
        protected override void OnUpdate()
        {
            var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
            //Debug.Log(String.Format("receive frames {0:d}", frameCount));
            Entities.ForEach((Entity switchEntity, int entityInQueryIndex, ref SwitchData switchData,ref DynamicBuffer<AdjacencyEntry> adjacencyEntries, ref DynamicBuffer<TopoEntry> topoEntries, ref DynamicBuffer<ReceivedPacketEntry> receivedPacketEntries, ref DynamicBuffer<LSAIdentifier> lSAIdentifiers) =>
            {
                //Debug.Log(String.Format("swicth id {0:d} receivedPackets {1:d}", switchData.switch_id, receivedPacketEntries.Length));
                if (receivedPacketEntries.Length > 0)
                {
                    var receivedPackets = receivedPacketEntries.AsNativeArray();
                    for (int i = 0; i < receivedPackets.Length; i++)
                    {
                        var packet = receivedPackets[i].ospfPacket;
                        //根据switch当前状态回应收到的HELLO报文
                        if (packet.ospfPacketType == OSPFPacketType.HELLO)
                        {
                            //Debug.Log(String.Format("Hello from {0:d} to {1:d} is received.", packet.source_id, packet.dest_id));
                            var transitter = ecb.CreateEntity(entityInQueryIndex);
                            ecb.AddComponent(entityInQueryIndex, transitter, new OSPFPacket
                            {
                                ospfPacketType = OSPFPacketType.HELLORES,
                                transitting_frame = 0,
                                source_id = switchData.switch_id,
                                dest_id = packet.source_id,
                                ack = switchData.isWorking ? true : false
                            });
                            //Debug.Log(String.Format("HelloRes from {0:d} to {1:d} starts sending.", switchData.switch_id, packet.source_id));
                        } 
                        else if (packet.ospfPacketType == OSPFPacketType.HELLORES)
                        {
                           // Debug.Log(String.Format("HelloRes from {0:d} to switch {1:d} is received.", packet.source_id, packet.dest_id));
                            var adjcencies = adjacencyEntries.AsNativeArray();
                            for (int j = 0; j < adjcencies.Length; j++)
                            {
                                var adjcency = adjcencies[j];
                                //找到发送此HELLORES保文的邻居
                                if (adjcency.node_id == packet.source_id)
                                {
                                    //判断链路状态是否发生改变
                                    if (packet.ack && adjcency.ospfStatus == OSPFStatus.DOWN || !packet.ack && adjcency.ospfStatus == OSPFStatus.TWO_WAY)
                                    {
                                        adjcency.ospfStatus = adjcency.ospfStatus == OSPFStatus.DOWN ? OSPFStatus.TWO_WAY : OSPFStatus.DOWN;
                                        adjcencies[j] = adjcency;
                                        var lid = adjcency.node_id > switchData.switch_id ? switchData.switch_id : adjcency.node_id;
                                        var gid = lid == switchData.switch_id ? adjcency.node_id : switchData.switch_id;
                                        Debug.Log(String.Format("Link state between {0:d} and {1:d} is changed.", lid, gid));
                                        var topoEntry = new TopoEntry
                                        {
                                            //以一个随机生成的随机数标识此条拓扑信息，避免洪泛陷入循环
                                            identifier = UnityEngine.Random.Range(0, int.MaxValue),
                                            vertexl_id = lid,
                                            vertexg_id = gid,
                                            isValid = packet.ack
                                        };
                                        //通告所有邻居
                                       for (int k = 0; k < adjcencies.Length; k++)
                                        {
                                            if (adjcencies[k].type == NodeType.HOST) continue;
                                            var transitter = ecb.CreateEntity(entityInQueryIndex);
                                            ecb.AddComponent(entityInQueryIndex, transitter, new OSPFPacket
                                            {
                                                ospfPacketType = OSPFPacketType.LSA,
                                                source_id = switchData.switch_id,
                                                dest_id = adjcencies[k].node_id,
                                                transitting_frame = 0,
                                                topoEntry = topoEntry
                                            });
                                        }
                                    }
                                    break;
                                }
                            }
                        } 
                        else if (packet.ospfPacketType == OSPFPacketType.LSA)
                        {
                            var topos = topoEntries.AsNativeArray();
                            var receivedTopo = packet.topoEntry;
                            bool shouldAppend = true;
                            bool shouldPassOn = false;
                            for (int j = 0; j < topos.Length; j++)
                            {
                                var topo = topos[j];
                                //若此拓扑信息相关的两个节点已在自身拓扑表中
                                if (topo.vertexl_id == receivedTopo.vertexl_id && topo.vertexg_id == receivedTopo.vertexg_id)
                                {
                                    shouldAppend = false;
                                    //若此topo信息此前未接收过
                                    var identifiers = lSAIdentifiers.AsNativeArray();
                                    bool isDuplicated = false;
                                    for (int k = 0; k < identifiers.Length; k++)
                                    {
                                        if (identifiers[k].identifier == receivedTopo.identifier)
                                        {
                                            isDuplicated = true;
                                            break;
                                        }
                                    }
                                    if (!isDuplicated)
                                    {
                                        lSAIdentifiers.Add(new LSAIdentifier
                                        {
                                            identifier = receivedTopo.identifier
                                        });
                                        topo.isValid = receivedTopo.isValid;
                                        topos[j] = topo;
                                        shouldPassOn = true;
                                    }
                                    break;
                                }
                            }
                            if (shouldAppend)
                            {
                                lSAIdentifiers.Add(new LSAIdentifier
                                {
                                    identifier = receivedTopo.identifier
                                });
                                topoEntries.Add(receivedTopo);
                                shouldPassOn = true;
                            }
                            if (shouldPassOn)
                            {
                                if (packet.topoEntry.isValid)
                                {
                                    //Debug.Log(String.Format("switch {0:d} is informed that LinkEntry (lid {1:d}, gid {2:d}, identifier {3:d}) transited to a valid state.", packet.dest_id, packet.topoEntry.vertexl_id, packet.topoEntry.vertexg_id, packet.topoEntry.identifier));
                                } else
                                {
                                    //Debug.Log(String.Format("switch {0:d} is informed that LinkEntry (lid {1:d}, gid {2:d}, identifier {3:d}) transited to a invalid state.", packet.dest_id, packet.topoEntry.vertexl_id, packet.topoEntry.vertexg_id, packet.topoEntry.identifier));
                                }
                                var adjcencies = adjacencyEntries.AsNativeArray();
                                for (int k = 0; k < adjcencies.Length; k++)
                                {
                                    if (adjcencies[k].type == NodeType.HOST) continue;
                                    var transitter = ecb.CreateEntity(entityInQueryIndex);
                                    ecb.AddComponent(entityInQueryIndex, transitter, new OSPFPacket
                                    {
                                        ospfPacketType = OSPFPacketType.LSA,
                                        source_id = packet.source_id,
                                        dest_id = adjcencies[k].node_id,
                                        transitting_frame = 0,
                                        topoEntry = receivedTopo
                                    });
                                }
                            }
                        }
                    }
                    receivedPacketEntries.Clear();
                }
            }).ScheduleParallel();
            //Debug.Log(String.Format("receive frames {0:d} end", frameCount));
            ecbSystem.AddJobHandleForProducer(Dependency);
        }
    }
}