using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using System;
using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Samples.DONSSystem
{
    public struct FlowSpawner : IComponentData
    {
        public Entity Prefab;
        public float3 SpawnPos;
        public int simulation_duration_per_update;
        public int OutputPort_index;
        public int host_id;
        public int host_node;
        public int ID;
        public long simulator_time; //nanosecond
        public int LinkRate;       //Gbps
        public long TX_nums;        //bytes
        public int Frames;
        public int LinkDelay;
        public Entity peer;

        //congestion control
        public long snd_nxt, snd_una;
        public long cwnd;
        public int ssthresh;
        public long last_ACK_time; //nanosecond
        //DCTCP
        public double dctcp_alpha;
        public double dctcp_G;
        public long dctcp_WindowEnd;
        public long dctcp_bytesAcked;
        public long dctcp_bytesECNMarked;
    }

    public enum PacketType
    {
        ACK,
        TCP,
        LSA
    }
    public struct Packet : IBufferElementData
    {
        //public int l3Prot;
        public PacketType type;
        public long seq_num;
        public int flow_ID;
        public int dest_id;
        public int DSCP;
        public int payload_length;    //bytes
        public int pkt_length;    //bytes
        public long enqueue_time;  //nanosecond
        public long dequeue_time;  //nanosecond
        public int IP_CE;              //Congestion Experienced
        public int TCP_ECE;
        public int content_id;
    }

    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(ForwardSystem))]
    public partial class SendSystem : SystemBase
    {
        //private BeginSimulationEntityCommandBufferSystem ecbSystem;
        private EndFixedStepSimulationEntityCommandBufferSystem ecbSystem;

        protected override void OnCreate()
        {
            //ecbSystem = World.GetExistingSystem<BeginSimulationEntityCommandBufferSystem>();
            ecbSystem = World.GetExistingSystem<EndFixedStepSimulationEntityCommandBufferSystem>();
            this.Enabled = false;
        }

        protected override void OnUpdate()
        {
            var ecb = ecbSystem.CreateCommandBuffer().AsParallelWriter();
            Entities
                .WithName("FlowSpawner")
                .ForEach((Entity SenderEntity, int entityInQueryIndex, ref FlowSpawner spawner, ref DynamicBuffer<Packet> ack_queue) =>
                {
                    spawner.Frames += 1; //5 frames for warm up

                    if(spawner.Frames > 5 && spawner.Frames%2==1 && spawner.OutputPort_index >= 0){
                        //Due to IngressPort to EgressPort doesnt comsume time, but needs two updates. Detailed see system_clock_sync.xlsx
                        //The sender's two updates as one update to perform the computation, in order to synchronize the clock with the IngressPort & EgressPort
                        var p = new Packet
                        {
                            type = PacketType.TCP,//l3Prot = 0x06, //TCP: 0x06, UDP: 0x11
                            flow_ID = spawner.host_id,
                            dest_id = spawner.host_node-1-spawner.host_id,//spawner.host_node-1-spawner.host_id, //test
                            DSCP = 1,
                            enqueue_time = 0,
                            payload_length = 1452,
                            pkt_length = 1500,
                            seq_num = 0,
                            IP_CE = 0,
                            TCP_ECE = 0,
                        };
                        //Debug.Log(String.Format("sender: {0:d} {1:d} {2:D} {3:d}", spawner.host_id, spawner.Frames, spawner.TX_nums, spawner.simulator_time));
                        //var queueBuffer = GetBufferFromEntity<Packet>(false)[targetEntities[dest_index]];  //find the SwitchLink
                        //var linkComponent = GetComponentDataFromEntity<SwitchLink>(false)[targetEntities[0]]; // true = read only!
                        //int nums = spawner.LinkRate * 100 / (int)(1.5*8); //100Gbps 100us
                        var EndTime = spawner.simulator_time + spawner.simulation_duration_per_update - spawner.simulator_time%spawner.simulation_duration_per_update; //100us
                        while(true && spawner.host_id != p.dest_id){
                            if(spawner.host_id >= (spawner.host_node>>1) && ack_queue.Length == 0)
                                break;
                            if(spawner.simulator_time >= EndTime)
                                break;
                            
                            if(ack_queue.Length > 0 && ack_queue[0].dest_id == spawner.host_id){
                                //Send: handle ACK events
                                var ack_p = ack_queue.ElementAt(0);
                                spawner.last_ACK_time = spawner.simulator_time;
                                if(spawner.simulator_time >= ack_p.enqueue_time || spawner.snd_nxt - spawner.snd_una >= spawner.cwnd) {
                                    if(ack_p.seq_num > spawner.snd_una) {
                                        var bytesAcked = ack_p.seq_num - spawner.snd_una;
                                        spawner.snd_una = ack_p.seq_num;
                                        spawner.cwnd += 1500;         //Additive Increase
                                        //DCTCP
                                        // spawner.dctcp_bytesAcked += bytesAcked;
                                        // if(ack_p.TCP_ECE == 1){
                                        //     spawner.dctcp_bytesECNMarked += bytesAcked;
                                        // }
                                        // if(ack_p.seq_num > spawner.dctcp_WindowEnd && spawner.dctcp_bytesECNMarked > 0) {
                                        //     float M = spawner.dctcp_bytesECNMarked / spawner.dctcp_bytesAcked;
                                        //     spawner.dctcp_alpha = spawner.dctcp_alpha*(1-spawner.dctcp_G) + M*spawner.dctcp_G;
                                        //     spawner.dctcp_WindowEnd = spawner.snd_nxt;
                                        //     spawner.dctcp_bytesAcked = 0;
                                        //     spawner.dctcp_bytesECNMarked = 0;
                                        //     spawner.cwnd = (int)(spawner.cwnd * (1 - spawner.dctcp_alpha/2));
                                        // }
                                        // if(spawner.dctcp_bytesECNMarked == 0){
                                        //     spawner.cwnd += 1452;
                                        // }
                                    } else {
                                        //Droped, retransmission
                                        spawner.snd_nxt = spawner.snd_una;
                                        //Debug.Log(String.Format("Droped, retransmission: {0:d} {1:d}", spawner.host_id, spawner.cwnd));
                                        spawner.cwnd = spawner.cwnd >> 1;   //Multiplicative Decrease
                                    }
                                    ack_queue.RemoveAt(0);
                                }
                            } else if(ack_queue.Length > 0) {
                                //Receiver: send ACK out
                                var ack_p = ack_queue.ElementAt(0);
                                if(ack_p.enqueue_time >= EndTime || spawner.simulator_time >= EndTime)
                                    break;
                                ack_p.dequeue_time = Math.Max(ack_p.enqueue_time, spawner.simulator_time) + ack_p.pkt_length * 8 / spawner.LinkRate;
                                ack_p.enqueue_time = ack_p.dequeue_time + spawner.LinkDelay; //link delay
                                spawner.simulator_time = ack_p.dequeue_time;
                                //Debug.Log(String.Format("send ACK: {0:d} {1:d} {2:D} {3:d}", spawner.host_id, ack_p.flow_ID, ack_p.seq_num, ack_p.dequeue_time));
                                ecb.AppendToBuffer<Packet>(entityInQueryIndex, spawner.peer, ack_p); //enqueue
                                ack_queue.RemoveAt(0);
                                continue;
                            }
                            //CC
                            if(p.type == PacketType.TCP && spawner.simulator_time - spawner.last_ACK_time > 100 * 1000)
                            { //p.l3Prot == 0x06
                                //RTO timeout retransmission
                                spawner.snd_nxt = spawner.snd_una;
                                //Debug.Log(String.Format("RTO timeout retransmission: {0:d} {1:d}", spawner.host_id, spawner.cwnd));
                                spawner.cwnd = spawner.cwnd >> 1;   //Multiplicative Decrease
                            }
                            if(spawner.cwnd == 0){
                                spawner.cwnd = 1500 * 10;
                            }
                            //p.l3Prot == 0x06
                            if (p.type == PacketType.TCP && spawner.snd_nxt - spawner.snd_una >= spawner.cwnd) { //TCP congestion control
                                //Debug.Log(String.Format("sender cannot TX: {0:d} {1:d}", spawner.host_id, spawner.cwnd));
                                break; //cannot TX now, waiting for the increase of cwnd
                            }
                            // if(spawner.TX_nums >= 2904000000) //for DEBUG
                            //     break;
                            p.dequeue_time = spawner.simulator_time + p.pkt_length * 8 / spawner.LinkRate; //TX_delay: p.pkt_length * 8 / spawner.LinkRate
                            p.enqueue_time = p.dequeue_time + spawner.LinkDelay; //link delay
                            p.seq_num = spawner.snd_nxt;
                            spawner.simulator_time = p.dequeue_time;
                            ecb.AppendToBuffer<Packet>(entityInQueryIndex, spawner.peer, p); //enqueue
                            spawner.TX_nums += p.payload_length;
                            spawner.snd_nxt += p.payload_length;
                            //break;  //for DEBUG
                        }
                        if(spawner.simulator_time < EndTime)
                            spawner.simulator_time = EndTime;
                    }
                }).ScheduleParallel();
            ecbSystem.AddJobHandleForProducer(Dependency);
        }
    }
}
