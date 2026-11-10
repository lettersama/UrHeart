namespace MrTGame.Heartbeat
{
    /// <summary>心跳系统工作模式(设计文档 §3.1)。</summary>
    public enum HeartbeatMode
    {
        Idle,          // 不接受输入,波形只做环境展示
        FreeTap,       // 自由节拍:按 baseBpm 持续判定(OS主体默认)
        Qte,           // 限定段落:CPR教程、排异事件——由调用方统计连击(见 CprMinigame)
        RhythmFollow   // 变速跟随:新节拍机制③"与教授心跳共鸣" [草案]
    }

    public enum BeatJudgement { Miss, Good, Perfect }

    public readonly struct BeatResult
    {
        public readonly BeatJudgement Judgement;

        /// <summary>相对理想拍点的偏移(秒,负=早 正=晚)。Miss(未按)时为 NaN。</summary>
        public readonly float Offset;

        public BeatResult(BeatJudgement judgement, float offset)
        {
            Judgement = judgement;
            Offset = offset;
        }
    }
}
