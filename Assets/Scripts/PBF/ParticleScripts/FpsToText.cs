using UnityEngine;
using UnityEngine.UI;

namespace Cleverous.Shared
{

    public class FpsToText : MonoBehaviour
    {
        [Header("// Sample Groups of Data ")]
        [Tooltip("Instead of reporting a specific framerate, this will sample many frames and report the average.")]
        public bool GroupSampling;

        [Tooltip("If Group Sampling is on, how many frames would you want to sample to report an average on?")]
        [Range(0, 20)]
        public int SampleSize;

        [Header("// Config ")]
        [Tooltip("The Text Component you want the result pushed to. If you're using TMP then you need to change this in the code to a TMP_Text component instead.")]
        public Text TargetText;

        [Tooltip("How often (in frames) do you want to update the Text component?")]
        [Range(1, 20)]
        public int UpdateTextEvery;

        [Tooltip("This sets how many numbers are buffered into memory as strings in order to obtain zero allocations at runtime.\n\nAlthough this is trivial in memory usage, realistically, there's no reason to be over 1000.")]
        [Range(0, 1000)]
        public int NumberBufferSize;

        [Header("// Color Config ")]
        [Tooltip("Optionally change the color of the TargetText based on FPS performance.")]
        public bool UseColors;
        [Tooltip("If the framerate is above 'OkayBelow' it will be the 'Good' color.")]
        public Color Good;
        [Tooltip("If the framerate is below 'OkayBelow' it will be the 'Okay' color.")]
        public Color Okay;
        [Tooltip("If the framerate is below 'BadBelow' it will be the 'Bad' color.")]
        public Color Bad;
        [Tooltip("Threshold for defining an 'okay' framerate. Below this value is considered okay, but not high enough to be good, and not low enough to be bad.")]
        public int OkayBelow;
        [Tooltip("Threshold for defining an 'bad' framerate. Below this value is considered bad.")]
        public int BadBelow;

        public int Framerate { get; private set; }

        protected int[] FpsSamples;
        protected int SampleIndex;
        protected int TextUpdateIndex;

        private string m_localfps;

        private static string[] m_numbers;

        private float m_lastRealtime = -1f;

        private float UnclampedDeltaTime
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                float delta = m_lastRealtime < 0f ? Time.deltaTime : now - m_lastRealtime;

                m_lastRealtime = now;

                return delta > 0f ? delta : 0.0001f;
            }
        }

        protected virtual void Reset()
        {
            GroupSampling = true;
            SampleSize = 20;
            UpdateTextEvery = 1;
            UseColors = true;

            Good = Color.green;
            Okay = Color.yellow;
            Bad = Color.red;

            OkayBelow = 60;
            BadBelow = 30;

            NumberBufferSize = 1000;
        }

        protected virtual void Start()
        {
            m_numbers = new string[NumberBufferSize];
            for (int i = 0; i < NumberBufferSize; i++) m_numbers[i] = i.ToString();

            FpsSamples = new int[SampleSize];
            for (int i = 0; i < FpsSamples.Length; i++) FpsSamples[i] = 1;
            if (!TargetText) enabled = false;
        }

        protected virtual void Update()
        {
            if (GroupSampling) Group();
            else SingleFrame();

            m_localfps = m_numbers[Framerate];

            SampleIndex = SampleIndex < SampleSize - 1 ? SampleIndex + 1 : 0;
            TextUpdateIndex = TextUpdateIndex > UpdateTextEvery ? 0 : TextUpdateIndex + 1;
            if (TextUpdateIndex == UpdateTextEvery) TargetText.text = m_localfps;

            if (!UseColors) return;
            if (Framerate < BadBelow)
            {
                TargetText.color = Bad;
                return;
            }
            TargetText.color = Framerate < OkayBelow ? Okay : Good;
        }

        protected virtual void SingleFrame()
        {
            Framerate = Mathf.Clamp((int)(1f / UnclampedDeltaTime), 0, m_numbers.Length - 1);
        }

        protected virtual void Group()
        {
            FpsSamples[SampleIndex] = Mathf.Clamp((int)(1f / UnclampedDeltaTime), 0,
                                                  m_numbers.Length - 1);

            Framerate = 0;
            bool loop = true;
            int i = 0;
            while (loop)
            {
                if (i == SampleSize - 1) loop = false;
                Framerate += FpsSamples[i];
                i++;
            }
            Framerate /= FpsSamples.Length;
        }
    }
}
