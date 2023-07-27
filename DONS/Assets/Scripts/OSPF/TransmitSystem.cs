using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Samples.DONSSystem
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(HelloSystem))]
    public partial class TransmitSystem : SystemBase
    {
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;
        private int frame = 0;
        protected override void OnCreate()
        {
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }
        protected override void OnUpdate()
        {
            //Debug.Log(String.Format("transmit frames {0:d}", frame));
            //Debug.Log(String.Format("transmit frames {0:d}", frame));
            var ecb = ecbSystem.CreateCommandBuffer();
            var ecbr = ecb.AsParallelWriter();
            var switchEntities = GetEntityQuery(ComponentType.ReadOnly<SwitchData>()).ToEntityArray(Allocator.Temp);
            var host_num = GetComponent<SwitchData>(switchEntities[0]).host_node;
            var fc = frame;
            Entities.ForEach((Entity transmitterEntity, int entityInQueryIndex, ref OSPFPacket ospfPacket) =>
            {
                ospfPacket.transitting_frame++;
                if (ospfPacket.transitting_frame == 10)
                {
                    if (ospfPacket.dest_id < host_num)
                    {
                        if (ospfPacket.ospfPacketType == OSPFPacketType.HELLO)
                        {
                            Debug.Log(String.Format("Hello from {0:d} to {1:d} is received.", ospfPacket.source_id, ospfPacket.dest_id));
                            var transitter = ecb.CreateEntity();
                            ecb.AddComponent<OSPFPacket>(transitter, new OSPFPacket
                            {
                                ospfPacketType = OSPFPacketType.HELLORES,
                                transitting_frame = 0,
                                source_id = ospfPacket.dest_id,
                                dest_id = ospfPacket.source_id,
                                ack = true
                            });
                            Debug.Log(String.Format("HelloRes from {0:d} to {1:d} starts sending.", ospfPacket.dest_id, ospfPacket.source_id));
                        }
                    }
                    else
                    {
                        ecbr.AppendToBuffer<ReceivedPacketEntry>(entityInQueryIndex, switchEntities[ospfPacket.dest_id - host_num], new ReceivedPacketEntry
                        {
                            ospfPacket = ospfPacket
                            /*
                             * new OSPFPacket
                            {
                                ospfPacketType = ospfPacket.ospfPacketType,
                                topoEntry = ospfPacket.topoEntry,
                                source_id = ospfPacket.source_id,
                                dest_id = ospfPacket.dest_id,
                                ack = ospfPacket.ack
                            }*/
                        });
                    }
                    //Debug.Log(String.Format("swicth id {0:d} receivedPackets {1:d}", switchData.switch_id, receivedPacketEntries.Length));
                    //Debug.Log("before destroy");
                    ecb.DestroyEntity(transmitterEntity);
                    //Debug.Log("after destroy");
                }
            }).ScheduleParallel();
            frame++;
            ecbSystem.AddJobHandleForProducer(Dependency);
        }
    }
}