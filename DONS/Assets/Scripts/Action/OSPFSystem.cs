using Assets.Advanced.DONS.Base;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;


namespace Samples.DONSSystem {
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(ScheduleRRSystem))]
    [UpdateBefore(typeof(ScheduleFIFOSystem))]
    public partial class OSPFSystem : SystemBase
    {
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;
        private int frameCount = -1;
        private int host_nums = -1;
        private int epoach = int.MaxValue - 1;
        private NativeArray<Entity> switchEntities;
        private Entity OSPFTransmitter;
        private int transmittingFrame = 10;
        
        private bool init = false;
        protected override void OnCreate()
        {
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }
        protected override void OnUpdate()
        {
            if (!init)
            {
                var switchQuery = GetEntityQuery(ComponentType.ReadOnly<SwitchData>());
                OSPFTransmitter = GetEntityQuery(ComponentType.ReadOnly<OSPFTransmitterData>()).ToEntityArray(Allocator.Temp)[0];
                switchEntities = switchQuery.ToEntityArray(Allocator.Temp);
                host_nums = switchQuery.ToComponentDataArray<SwitchData>(Allocator.Temp)[0].host_node;
                init = true;
                Debug.Log(String.Format("host_num: {0:d}", host_nums));
                Debug.Log(String.Format("switch_num: {0:d}", switchEntities.Length));
            }
            TransmitLSAPackets();
            UpdateRoutingTable();
        }

        Entity getSwitchEntityById(int switchId)
        {
            return switchEntities[switchId - host_nums];
        }
        void TransmitLSAPackets()
        {
            var packetsInTransit = GetBuffer<LSAPacket>(OSPFTransmitter);
            //Debug.Log(String.Format("TransmitLSAPackets: {0:d}", packetsInTransit.Length));
            for (int i = 0; i < packetsInTransit.Length; i++)
            {
                var packet = packetsInTransit[i];
                packet.transfering_frame++;
                packetsInTransit[i] = packet;
                //包送达
                if (packet.transfering_frame == transmittingFrame)
                {
                    Debug.Log(String.Format("ISA from {0:d} to {1:d} is received", packet.source_id, packet.dest_id));
                    GetBuffer<LSAPacket>(getSwitchEntityById(packet.dest_id)).Add(packet);
                    packetsInTransit.RemoveAt(i);
                    i--;
                }
            }
        }
        void UpdateRoutingTable()
        {
            //每个epoch重新生成一次forwarding_table
            frameCount++;
            frameCount %= epoach;
            if (frameCount == 0)
            {
                Entities.ForEach((Entity switchEntity, int entityInQueryIndex, ref SwitchData switchData) =>
                {
                    switchData.isUpdating = true;
                }).ScheduleParallel();
            }
            //预先读取所有RoutingTable
            var switchEntities = GetEntityQuery(ComponentType.ReadOnly<SwitchData>()).ToEntityArray(Allocator.Temp);
            NativeArray<DynamicBuffer<RoutingEntry>> routingTableArray = new NativeArray<DynamicBuffer<RoutingEntry>>(switchEntities.Length,Allocator.Temp);
            for (int i = 0; i < switchEntities.Length; i++)
            {
                routingTableArray[i] = GetBuffer<RoutingEntry>(switchEntities[i], true);
            }
            //预先读取所有RoutingTable结束
            var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
            Entities.ForEach((Entity SwitchEntity, int entityInQueryIndex, ref DynamicBuffer<AdjacencyEntry> adjacencyEntriesBuffer, ref DynamicBuffer<RoutingEntry> routingEntriesBuffer, ref DynamicBuffer<LSAPacket> receivedLSAPackets, ref SwitchData switchData) =>
            {
                var adjacencyEntries = adjacencyEntriesBuffer.AsNativeArray();
                var routingEntries = routingEntriesBuffer.AsNativeArray();
                if (switchData.isUpdating)
                {
                    //Debug.Log(String.Format("Switch {0:d}", switchData.switch_id));
                    //清空routing_table,RoutingTable目前为n项，下标与dest_id相同
                    for (int i = 0; i < routingEntries.Length; i++)
                    {
                        var routingEntry = routingEntries[i];
                        routingEntry.isExpired = true;
                        routingEntries[i] = routingEntry;
                    }
                    //重新初始化routing_table
                    for (int i = 0; i < adjacencyEntries.Length; i++)
                    {
                        var id = adjacencyEntries[i].node_id;
                        //Debug.Log(String.Format("Adjacent node: id {0:d}", id));
                        var routingEntry = routingEntries[id];
                        routingEntry.isExpired = false;
                        routingEntry.next_hop = id;
                        routingEntry.distance = 1;
                        routingEntries[id] = routingEntry;
                    }
                    //RoutingTableUpdated(switchData.switch_id,ref ecb, in adjacencyEntries, in routingEntries);
                    Debug.Log(String.Format(" switch {0:d} routing_table: ", switchData.switch_id));
                    for (int i = 0; i < routingEntries.Length; i++)
                    {
                        var routingEntry = routingEntries[i];
                        if (routingEntry.isExpired) continue;
                        Debug.Log(String.Format(" dest_id: {0:d}, next_hop: {1:d}, distance: {2:d} ", routingEntry.dest_id, routingEntry.next_hop, routingEntry.distance));
                    }
                    Debug.Log("-----------------------");
                    for (int i = 0; i < adjacencyEntries.Length; i++)
                    {
                        var adjencency = adjacencyEntries[i];
                        if (adjencency.type == NodeType.SWITCH)
                        {
                            var packet = new LSAPacket
                            {
                                dest_id = adjencency.node_id,
                                source_id = switchData.switch_id,
                                transfering_frame = 0
                            };
                            ecb.AppendToBuffer(entityInQueryIndex, switchData.transmitter, packet);
                        }
                    }

                    switchData.isUpdating = false;
                }
                if (receivedLSAPackets.Length > 0)
                {
                    //Debug.Log("updating using received LSAPackets");
                    bool isUpdated = false;
                    for (int i = 0; i < receivedLSAPackets.Length; i++)
                    {
                        //通过包中source_id得到对应的switchEntity并取得其RoutingTable
                        var source_id = receivedLSAPackets[i].source_id;
                        var adjRoutingEntries = routingTableArray[source_id - switchData.host_node];//GetBuffer<RoutingEntry>(getSwitchEntityById(source_id), true).AsNativeArray();
                        //对比更新
                        for (int k = 0; k < adjRoutingEntries.Length; k++)
                        {
                            var adjRoutingEntry = adjRoutingEntries[k];
                            if (adjRoutingEntry.isExpired || k == switchData.switch_id) continue;
                            var routingEntry = routingEntries[k];
                            if (routingEntry.isExpired || routingEntry.distance > adjRoutingEntry.distance + 1)
                            {
                                routingEntry.next_hop = source_id;
                                routingEntry.isExpired = false;
                                routingEntry.distance = adjRoutingEntry.distance + 1;
                                routingEntries[k] = routingEntry;
                                isUpdated = true;
                            }
                        }
                    }
                    //若发生变化则再次向外发送
                    if (isUpdated)
                    {
                        //RoutingTableUpdated(switchData.switch_id, ref ecb, in adjacencyEntries, in routingEntries);
                        Debug.Log(String.Format(" switch {0:d} routing_table: ", switchData.switch_id));
                        for (int i = 0; i < routingEntries.Length; i++)
                        {
                            var routingEntry = routingEntries[i];
                            if (routingEntry.isExpired) continue;
                            Debug.Log(String.Format(" dest_id: {0:d}, next_hop: {1:d}, distance: {2:d} ", routingEntry.dest_id, routingEntry.next_hop, routingEntry.distance));
                        }
                        Debug.Log("-----------------------");
                        for (int i = 0; i < adjacencyEntries.Length; i++)
                        {
                            var adjencency = adjacencyEntries[i];
                            if (adjencency.type == NodeType.SWITCH)
                            {
                                var packet = new LSAPacket
                                {
                                    dest_id = adjencency.node_id,
                                    source_id = switchData.switch_id,
                                    transfering_frame = 0
                                };
                                ecb.AppendToBuffer(entityInQueryIndex, switchData.transmitter, packet);
                            }
                        }
                        //RoutingTableUpdated
                    }
                    receivedLSAPackets.Clear();
                }
            }).ScheduleParallel();
            ecbSystem.AddJobHandleForProducer(Dependency);
        }

        void RoutingTableUpdated(int switchId, ref EntityCommandBuffer.ParallelWriter ecb, in NativeArray<AdjacencyEntry> adjacencyEntries, in NativeArray<RoutingEntry> routingEntries)
        {
            LogRoutingTable(switchId, in routingEntries);
            for (int i = 0; i < adjacencyEntries.Length; i++)
            {
                var adjencency = adjacencyEntries[i];
                if (adjencency.type == NodeType.SWITCH)
                {
                    LinkStateAdvertisement(adjencency.node_id, switchId, ref ecb);
                }
            }
        }

        void LogRoutingTable(int senderId, in NativeArray<RoutingEntry> routingEntries)
        {
            Debug.Log(String.Format(" switch {0:d} routing_table: ", senderId));
            for (int i = 0; i < routingEntries.Length; i++)
            {
                var routingEntry = routingEntries[i];
                if (routingEntry.isExpired) continue;
                Debug.Log(String.Format(" dest_id: {0:d}, next_hop: {1:d}, distance: {2:d} ", routingEntry.dest_id, routingEntry.next_hop, routingEntry.distance));
            }
            Debug.Log("-----------------------");
        }

        void LinkStateAdvertisement(int receiverId, int senderId, ref EntityCommandBuffer.ParallelWriter ecb)
        {
            var packet = new LSAPacket
            {
                dest_id = receiverId,
                source_id = senderId,
                transfering_frame = 0
            };
            ecb.AppendToBuffer<LSAPacket>(0, OSPFTransmitter, packet);
        }
    }
}