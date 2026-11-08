using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UsageOrbit.UI
{
    // Mascotas en pixel art. En reposo miran al frente; al trabajar giran de lado y usan una laptop vista de perfil.
    // Cada cuadro se dibuja una vez y se reutiliza durante la animación.
    internal static class Mascots
    {
        public const int RiseSteps = 4;
        public const int WorkFrames = 16;
        private const int PixelSize = 4;

        private static readonly Dictionary<char, Color> Colors = new Dictionary<char, Color> {
            { 'O', Hex("#EE7B30") }, { 'D', Hex("#C25E20") }, { 'k', Hex("#1C1A1E") },
            { '#', Hex("#232D66") }, { 'b', Hex("#6E9BFA") }, { 'd', Hex("#4A6FD0") }, { 'c', Hex("#8FE6FF") },
            { 'M', Hex("#6F7786") }, { 'L', Hex("#AEB6C2") }, { 'B', Hex("#4E5563") }, { 'g', Hex("#EAF1FF") }, { 'G', Hex("#B9D4FF") }
        };

        // La cara se dibuja aparte para poder girarla hacia un lado.
        private static readonly string[] CodexBody = {
            "........#######........",
            "......##bbbbbbb##......",
            ".....#bbbbbbbbbbb#.....",
            "...###bbbbbbbbbbb###...",
            "..#bbbbbbbbbbbbbbbbb#..",
            ".#bbbbbbbbbbbbbbbbbbb#.",
            "#bbbbbbbbbbbbbbbbbbbbb#",
            "#bbb###############bbb#",
            "#dbb###############bbb#",
            "#dbb###############bbb#",
            "#dbb###############bbb#",
            ".#bb###############bb#.",
            ".#db###############bb#.",
            ".#db###############bb#.",
            ".#db###############bb#.",
            "..#dbb###########bbb#..",
            "...#ddbbbbbbbbbbbdd#...",
            "....##ddddddddddd##....",
            ".....#bbbbbbbbbbb#.....",
            "....#b#bbbbbbbbb#b#....",
            "...#bb#bcbbbbbbb#bb#...",
            "...#bb#bbcbbbbbb#bb#...",
            "...#dd#bcbbccbbb#dd#...",
            "....##.#ddddddd#.##....",
            ".......#bb#.#bb#.......",
            ".......#dd#.#dd#.......",
            ".......####.####......."
        };

        // Tapa abierta vista de perfil, desde la bisagra: se inclina hacia atrás, lejos de la mascota.
        private static readonly int[,] OpenLid = { { 0, 0 }, { 0, -1 }, { -1, -2 }, { -1, -3 }, { -2, -4 }, { -2, -5 }, { -3, -6 }, { -3, -7 } };

        private static readonly ImageSource[] ClaudeRise = Rise(true), CodexRise = Rise(false);
        private static readonly ImageSource[] ClaudeWork = Work(true), CodexWork = Work(false);

        public static ImageSource Rising(bool claude, int step) { return (claude ? ClaudeRise : CodexRise)[step]; }
        public static ImageSource Working(bool claude, int frame) { return (claude ? ClaudeWork : CodexWork)[frame]; }

        // Paso 0: de frente. 1: gira. 2: aparece la laptop cerrada. 3: la abre a medias. 4: abierta.
        private static ImageSource[] Rise(bool claude)
        {
            var frames = new ImageSource[RiseSteps + 1];
            frames[0] = Render(claude ? ClaudeFront() : CodexFront());
            for (int step = 1; step <= RiseSteps; step++)
                frames[step] = Render(Side(claude, step - 1, -1, false, 0));
            return frames;
        }
        private static ImageSource[] Work(bool claude)
        {
            var frames = new ImageSource[WorkFrames];
            for (int i = 0; i < WorkFrames; i++)
                frames[i] = Render(Side(claude, 3, i % 2, i % 4 < 2, Gaze(i)));
            return frames;
        }

        // Mirada al teclear: casi siempre a la pantalla, con un parpadeo y vistazos al teclado y a la laptop.
        // 0 normal, 1 parpadeo, 2 hacia abajo, 3 hacia la laptop.
        private static int Gaze(int frame)
        {
            if (frame == 6) return 1;
            if (frame >= 10 && frame <= 13) return 2;
            return frame >= 14 ? 3 : 0;
        }

        private static char[][] ClaudeFront()
        {
            char[][] canvas = Canvas(24);
            foreach (int x in new[] { 4, 8, 13, 17 })
            {
                Fill(canvas, x, 16, 2, 4, 'O');
                Fill(canvas, x + 2, 16, 1, 4, 'D');
            }
            Fill(canvas, 2, 4, 20, 12, 'O');
            Fill(canvas, 2, 15, 20, 1, 'D');
            Fill(canvas, 0, 8, 2, 4, 'O');
            Fill(canvas, 22, 8, 2, 4, 'O');
            Fill(canvas, 5, 7, 2, 3, 'k');
            Fill(canvas, 17, 7, 2, 3, 'k');
            return canvas;
        }
        private static char[][] CodexFront()
        {
            char[][] canvas = Canvas(27);
            Stamp(canvas, CodexBody, 2, 0);
            CodexFace(canvas, 8, 9, true, false);
            return canvas;
        }

        // lid: 0 sin laptop, 1 cerrada, 2 a medias, 3 abierta. arm: -1 en reposo, 0 o 1 alterna al teclear. gaze: ver Gaze.
        private static char[][] Side(bool claude, int lid, int arm, bool glow, int gaze)
        {
            char[][] canvas;
            if (claude)
            {
                // Claude mira a la izquierda, como en Claude Code: de perfil se ve bajo y ancho,
                // con la laptop a ese lado.
                canvas = Canvas(24);
                foreach (int x in new[] { 9, 12, 17, 20 })
                {
                    Fill(canvas, x, 17, 1, 3, 'O');
                    Fill(canvas, x + 1, 17, 1, 3, 'D');
                }
                Fill(canvas, 8, 7, 16, 10, 'O');
                Fill(canvas, 8, 16, 16, 1, 'D');
                if (gaze == 1)
                {
                    Fill(canvas, 11, 10, 2, 1, 'k');
                    Fill(canvas, 16, 10, 2, 1, 'k');
                }
                else
                {
                    int dx = gaze == 3 ? -1 : 0, dy = gaze == 2 ? 1 : 0;
                    Fill(canvas, 11 + dx, 8 + dy, 2, 3, 'k');
                    Fill(canvas, 16 + dx, 8 + dy, 2, 3, 'k');
                }
                Laptop(canvas, 3, 19, 1, lid, glow, 6);
                // Teclea con las dos manos: la cercana en naranja y la lejana en sombra suben y bajan por turnos.
                if (arm >= 0)
                {
                    Fill(canvas, 6, arm == 0 ? 14 : 16, 2, 2, 'D');
                    Fill(canvas, 4, arm == 0 ? 16 : 14, 2, 2, 'O');
                }
            }
            else
            {
                // Codex, en espejo, mira a la derecha con la laptop de ese lado.
                canvas = Canvas(27);
                Stamp(canvas, CodexBody, 0, 0);
                // El cursor parpadea y la cara sigue la misma mirada que Claude, hacia su laptop a la derecha.
                int dx = gaze == 3 ? 1 : 0, dy = gaze == 2 ? 1 : 0;
                CodexFace(canvas, 8 + dx, 9 + dy, arm < 0 || glow, gaze == 1);
                if (arm >= 0) Fill(canvas, 19, 22 + arm, 2, 2, 'b');
                Laptop(canvas, 23, 26, -1, lid, glow, 8);
            }
            return canvas;
        }
        // Con blink el símbolo > se cierra en una línea, como un parpadeo.
        private static void CodexFace(char[][] canvas, int x, int y, bool cursor, bool blink)
        {
            if (blink) Fill(canvas, x, y + 2, 3, 1, 'c');
            else Stamp(canvas, new[] { "c..", ".c.", "..c", ".c.", "c.." }, x, y);
            if (cursor) Fill(canvas, x + 6, y + 4, 4, 1, 'c');
        }

        // toward: 1 si la mascota está a la derecha de la bisagra, -1 si está a la izquierda.
        private static void Laptop(char[][] canvas, int hingeX, int floorY, int toward, int lid, bool glow, int length)
        {
            if (lid == 0) return;
            for (int i = 0; i < length; i++)
            {
                canvas[floorY][hingeX + i * toward] = 'B';
                if (i > 0) canvas[floorY - 1][hingeX + i * toward] = 'L';
            }
            int hingeY = floorY - 1;
            if (lid == 1)
            {
                for (int i = 0; i < length - 1; i++) canvas[hingeY - 1][hingeX + i * toward] = 'M';
                return;
            }
            if (lid == 2)
            {
                for (int i = 0; i < 5; i++) canvas[hingeY - i][hingeX + i * toward] = 'M';
                return;
            }
            for (int i = 0; i < OpenLid.GetLength(0); i++)
            {
                int x = hingeX + OpenLid[i, 0] * toward, y = hingeY + OpenLid[i, 1];
                canvas[y][x] = 'M';
                if (i > 0) canvas[y][x + toward] = glow ? 'g' : 'G';
            }
        }

        private static char[][] Canvas(int size)
        {
            var canvas = new char[size][];
            for (int y = 0; y < size; y++) canvas[y] = new string('.', size).ToCharArray();
            return canvas;
        }
        private static void Fill(char[][] canvas, int x, int y, int width, int height, char color)
        {
            for (int row = y; row < y + height; row++)
                for (int column = x; column < x + width; column++)
                    canvas[row][column] = color;
        }
        private static void Stamp(char[][] canvas, string[] art, int x, int y)
        {
            for (int row = 0; row < art.Length; row++)
                for (int column = 0; column < art[row].Length; column++)
                    if (art[row][column] != '.') canvas[y + row][x + column] = art[row][column];
        }
        private static ImageSource Render(char[][] canvas)
        {
            int size = canvas.Length * PixelSize;
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    char cell = canvas[y / PixelSize][x / PixelSize];
                    if (cell == '.') continue;
                    Color color = Colors[cell];
                    int offset = (y * size + x) * 4;
                    pixels[offset] = color.B; pixels[offset + 1] = color.G; pixels[offset + 2] = color.R; pixels[offset + 3] = 255;
                }
            }
            var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            image.Freeze();
            return image;
        }
        private static Color Hex(string value) { return (Color)ColorConverter.ConvertFromString(value); }
    }

    // Avanza la animación: gira y saca la laptop mientras hay actividad, y la guarda al terminar.
    internal sealed class MascotAnimator
    {
        private readonly bool claude;
        private int step, frame;
        public MascotAnimator(bool claude) { this.claude = claude; }
        public bool NeedsTicks(bool active) { return active || step > 0; }
        public ImageSource Current
        {
            get { return step == Mascots.RiseSteps && frame >= 0 ? Mascots.Working(claude, frame) : Mascots.Rising(claude, step); }
        }
        public void Advance(bool active)
        {
            if (!active)
            {
                frame = -1;
                if (step > 0) step--;
            }
            else if (step < Mascots.RiseSteps)
            {
                frame = -1;
                step++;
            }
            else frame = (frame + 1) % Mascots.WorkFrames;
        }
    }
}
