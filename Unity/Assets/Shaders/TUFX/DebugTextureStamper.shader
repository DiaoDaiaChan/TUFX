Shader "Hidden/TUFX/DebugTextureStamper"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _StampAlpha ("Stamp Alpha", Range(0, 1)) = 0.85
        _PartIndex ("Part Index", Float) = 0
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _StampAlpha;
            float _PartIndex;

            v2f Vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Procedural 5x7 block bitmap font decoder for "DEBUG"
            // Bits: 35 bits packed per character (7 rows x 5 cols)
            // Row 0 is top, Row 6 is bottom. Col 0 is left, Col 4 is right.
            bool SampleCharPixel(int charIndex, int col, int row)
            {
                if (col < 0 || col >= 5 || row < 0 || row >= 7) return false;
                int bitIndex = row * 5 + col;

                // Packed 35-bit masks:
                // 'D' (index 0)
                if (charIndex == 0)
                {
                    // 11110 / 10001 / 10001 / 10001 / 10001 / 10001 / 11110
                    static const int rowsD[7] = { 30, 17, 17, 17, 17, 17, 30 };
                    return ((rowsD[row] >> (4 - col)) & 1) != 0;
                }
                // 'E' (index 1)
                if (charIndex == 1)
                {
                    // 11111 / 10000 / 10000 / 11110 / 10000 / 10000 / 11111
                    static const int rowsE[7] = { 31, 16, 16, 30, 16, 16, 31 };
                    return ((rowsE[row] >> (4 - col)) & 1) != 0;
                }
                // 'B' (index 2)
                if (charIndex == 2)
                {
                    // 11110 / 10001 / 10001 / 11110 / 10001 / 10001 / 11110
                    static const int rowsB[7] = { 30, 17, 17, 30, 17, 17, 30 };
                    return ((rowsB[row] >> (4 - col)) & 1) != 0;
                }
                // 'U' (index 3)
                if (charIndex == 3)
                {
                    // 10001 / 10001 / 10001 / 10001 / 10001 / 10001 / 01110
                    static const int rowsU[7] = { 17, 17, 17, 17, 17, 17, 14 };
                    return ((rowsU[row] >> (4 - col)) & 1) != 0;
                }
                // 'G' (index 4)
                if (charIndex == 4)
                {
                    // 01111 / 10000 / 10000 / 10011 / 10001 / 10001 / 01110
                    static const int rowsG[7] = { 15, 16, 16, 19, 17, 17, 14 };
                    return ((rowsG[row] >> (4 - col)) & 1) != 0;
                }
                return false;
            }

            float4 Frag(v2f i) : SV_Target
            {
                float4 orig = tex2D(_MainTex, i.uv);
                float2 uv = i.uv;

                // Center coordinates [-0.5, 0.5]
                float2 p = uv - float2(0.5, 0.5);

                // --- 1. Center Banner Backdrop ---
                float inBanner = (abs(p.y) < 0.14) ? 1.0 : 0.0;
                float inBannerBorder = (abs(p.y) >= 0.13 && abs(p.y) < 0.14) ? 1.0 : 0.0;

                // --- 2. Hazard Diagonal Stripes across Banner ---
                float stripeCoord = (uv.x + uv.y) * 45.0;
                float stripe = frac(stripeCoord) > 0.5 ? 1.0 : 0.0;
                float3 hazardColor = lerp(float3(0.1, 0.1, 0.1), float3(1.0, 0.85, 0.0), stripe);

                // Top & Bottom hazard accent bars
                float topBar = (uv.y > 0.94 && uv.y < 0.98) ? 1.0 : 0.0;
                float botBar = (uv.y > 0.02 && uv.y < 0.06) ? 1.0 : 0.0;
                float accentBars = max(topBar, botBar);

                // --- 3. Bold "DEBUG" text in center ---
                // Banner text box: width 0.7, height 0.2
                // 5 letters + 4 spaces: 5 * 5 + 4 * 2 = 33 units total width, 7 units height
                float2 textUV = (p + float2(0.35, 0.08)) / float2(0.70, 0.16);
                float inText = 0.0;
                float inShadow = 0.0;

                if (textUV.x >= 0.0 && textUV.x <= 1.0 && textUV.y >= 0.0 && textUV.y <= 1.0)
                {
                    float charUnitX = textUV.x * 35.0;
                    float charUnitY = (1.0 - textUV.y) * 7.0;

                    int charSlot = int(charUnitX / 7.0);
                    int col = int(fmod(charUnitX, 7.0));
                    int row = int(charUnitY);

                    if (charSlot >= 0 && charSlot < 5 && col < 5)
                    {
                        inText = SampleCharPixel(charSlot, col, row) ? 1.0 : 0.0;
                        // Drop shadow
                        inShadow = SampleCharPixel(charSlot, col - 1, row - 1) ? 0.7 : 0.0;
                    }
                }

                // --- 4. Corner Framing Brackets ---
                float2 cornerUV = abs(p) * 2.0; // [0, 1] from center to edge
                float cornerFrame = ((cornerUV.x > 0.88 && cornerUV.y > 0.88) && (cornerUV.x > 0.96 || cornerUV.y > 0.96)) ? 1.0 : 0.0;

                // --- 5. Composite Layers ---
                float3 stampColor = orig.rgb;
                float stampMask = 0.0;

                // Apply banner background
                if (inBanner > 0.5)
                {
                    float3 bannerBg = lerp(float3(0.08, 0.08, 0.10), float3(0.85, 0.20, 0.10), inBannerBorder);
                    stampColor = bannerBg;
                    stampMask = 0.80;
                }

                // Apply accent bars
                if (accentBars > 0.5)
                {
                    stampColor = hazardColor;
                    stampMask = 0.90;
                }

                // Apply corner brackets
                if (cornerFrame > 0.5)
                {
                    stampColor = float3(0.0, 0.9, 1.0); // Cyan tech bracket
                    stampMask = 1.0;
                }

                // Apply drop shadow & bright yellow text
                if (inShadow > 0.0 && inText == 0.0)
                {
                    stampColor = float3(0.0, 0.0, 0.0);
                    stampMask = max(stampMask, 0.90);
                }
                if (inText > 0.5)
                {
                    stampColor = float3(1.0, 0.95, 0.2); // Brilliant yellow
                    stampMask = 1.0;
                }

                float3 finalColor = lerp(orig.rgb, stampColor, stampMask * _StampAlpha);
                return float4(finalColor, orig.a);
            }
            ENDHLSL
        }
    }
}
