using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Samples.DONSSystem
{
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateAfter(typeof(ReceiverACKSystem))]
    public partial class HelloSystem : SystemBase
    {
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;
        private EndSimulationEntityCommandBufferSystem m_EndSimulationEcbSystem;
        private int frameCount = -1;
        private int epoach = 50000;
        protected override void OnCreate()
        {
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }
        protected override void OnUpdate()
        {
            frameCount++;
            frameCount %= epoach;
            if (frameCount == 0)
            {
                var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
                Entities.ForEach((Entity switchEntity, int entityInQueryIndex, in DynamicBuffer<AdjacencyEntry> adjacencyEntries, in SwitchData switchData) =>
                {
                    var adjcencies = adjacencyEntries.AsNativeArray();
                    for (int i = 0; i < adjcencies.Length; i++)
                    {
                        var adjcency = adjcencies[i];
                        //Debug.Log(String.Format("Hello from {0:d} to {1:d} starts transmitting.", switchData.switch_id, adjcency.node_id));
                        Entity tranmitter = ecb.CreateEntity(entityInQueryIndex);
                        ecb.AddComponent(entityInQueryIndex, tranmitter, new OSPFPacket
                        {
                            ospfPacketType = OSPFPacketType.HELLO,
                            dest_id = adjcency.node_id,
                            source_id = switchData.switch_id,
                            transitting_frame = 0
                        });
                    }
                }).ScheduleParallel();
                ecbSystem.AddJobHandleForProducer(Dependency);
            }
        }

    }
}