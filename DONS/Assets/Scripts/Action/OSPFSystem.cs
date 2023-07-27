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
                OSPFTransmitter = GetEntityQuery(ComponentType.ReadOnly<OSPFTransmitterData>()).ToEntityArray(Allocator.TempJob)[0];
                switchEntities = switchQuery.ToEntityArray(Allocator.TempJob);
                host_nums = switchQuery.ToComponentDataArray<SwitchData>(Allocator.TempJob)[0].host_node;
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
            var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
            for (int j = 0; j < switchEntities.Length; j++)
            {
                var switchEntity = switchEntities[j];
                var adjacencyEntriesBuffer = GetBuffer<AdjacencyEntry>(switchEntity);
                var adjacencyEntries = adjacencyEntriesBuffer.AsNativeArray();
                var routingEntriesBuffer = GetBuffer<RoutingEntry>(switchEntity);
                var routingEntries = routingEntriesBuffer.AsNativeArray();
                var receivedLSAPackets = GetBuffer<LSAPacket>(switchEntity);
                var switchData = GetComponent<SwitchData>(switchEntity);
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
                    RoutingTableUpdated(switchData.switch_id, in adjacencyEntries, in routingEntries);
                    switchData.isUpdating = false;
                    SetComponent(switchEntity, switchData);
                }
                //对于每个收到的包
                if (receivedLSAPackets.Length > 0)
                {
                    //Debug.Log("updating using received LSAPackets");
                    bool isUpdated = false;
                    for (int i = 0; i < receivedLSAPackets.Length; i++)
                    {
                        //通过包中source_id得到对应的switchEntity并取得其RoutingTable
                        var source_id = receivedLSAPackets[i].source_id;
                        var adjRoutingEntries = GetBuffer<RoutingEntry>(getSwitchEntityById(source_id)).AsNativeArray();
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
                        RoutingTableUpdated(switchData.switch_id, in adjacencyEntries, in routingEntries);
                    }
                    receivedLSAPackets.Clear();
                }
            }
            ecbSystem.AddJobHandleForProducer(Dependency);
        }

        void RoutingTableUpdated(int switchId, in NativeArray<AdjacencyEntry> adjacencyEntries, in NativeArray<RoutingEntry> routingEntries)
        {
            LogRoutingTable(switchId, in routingEntries);
            for (int i = 0; i < adjacencyEntries.Length; i++)
            {
                var adjencency = adjacencyEntries[i];
                if (adjencency.type == NodeType.SWITCH)
                {
                    LinkStateAdvertisement(adjencency.node_id, switchId);
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

        void LinkStateAdvertisement(int receiverId, int senderId)
        {
            var buf = GetBuffer<LSAPacket>(OSPFTransmitter);
            //Debug.Log(String.Format("before link state advertisement {0:d}", buf.Length));
            buf.Add(
            new LSAPacket
            {
                dest_id = receiverId,
                source_id = senderId,
                transfering_frame = 0
            });
            //Debug.Log(String.Format("after link state advertisement {0:d}", buf.Length));
        }
    }
}