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
            var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
            var switchEntities = GetEntityQuery(ComponentType.ReadOnly<SwitchData>()).ToEntityArray(Allocator.TempJob);
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
                            //Debug.Log(String.Format("Hello from {0:d} to {1:d} is received.", ospfPacket.source_id, ospfPacket.dest_id));
                            var transitter = ecb.CreateEntity(entityInQueryIndex);
                            ecb.AddComponent<OSPFPacket>(entityInQueryIndex ,transitter, new OSPFPacket
                            {
                                ospfPacketType = OSPFPacketType.HELLORES,
                                transitting_frame = 0,
                                source_id = ospfPacket.dest_id,
                                dest_id = ospfPacket.source_id,
                                ack = true
                            });
                            //Debug.Log(String.Format("HelloRes from {0:d} to {1:d} starts sending.", ospfPacket.dest_id, ospfPacket.source_id));
                        }
                    }
                    else
                    {
                        //Debug.Log(String.Format("idx {0:d} limit {1:d}", ospfPacket.dest_id - host_num, switchEntities.Length));
                        ecb.AppendToBuffer<ReceivedPacketEntry>(entityInQueryIndex, switchEntities[ospfPacket.dest_id - host_num], new ReceivedPacketEntry
                        {
                            ospfPacket = ospfPacket
                        });
                    }
                    ecb.DestroyEntity(entityInQueryIndex, transmitterEntity);
                }
            }).ScheduleParallel();
            frame++;
            Dependency = switchEntities.Dispose(Dependency);
            ecbSystem.AddJobHandleForProducer(Dependency);
        }
    }
}