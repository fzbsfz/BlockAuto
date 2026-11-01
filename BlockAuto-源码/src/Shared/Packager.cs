using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BlockAuto
{
    /// <summary>
    /// 脚本打包工具（P3）：把 .block + 引用的模板 + BlockRunner.exe 打成一个 zip，
    /// 解压后目录内附带“双击运行.cmd”，接收方无需安装编辑器即可运行。
    /// </summary>
    public static class ScriptPackager
    {
        public sealed class PackResult
        {
            public string ZipPath;
            public int TemplateCount;
            public long SizeBytes;
            public List<string> Warnings = new List<string>();
        }

        /// <summary>
        /// 找出脚本引用的模板相对路径（去重）。
        /// v1.0 起不再限定「识图积木」：识图已并入鼠标点击，判断积木的「有图片」也会用到模板，
        /// 所以统一按「参数里有 template 且非空、非绝对路径」收集。
        /// </summary>
        public static List<string> CollectTemplates(ScriptDocument doc)
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Block b in doc.AllBlocks())
            {
                if (b.Params == null) continue;
                object raw;
                if (!b.Params.TryGetValue("template", out raw)) continue;
                string tpl = Json.GetString(b.Params, "template", "");
                if (string.IsNullOrEmpty(tpl)) continue;
                if (Path.IsPathRooted(tpl)) continue;
                string norm = tpl.Replace('\\', '/').TrimStart('/');
                if (norm.Length == 0) continue;
                if (seen.Add(norm)) list.Add(norm);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static PackResult Pack(string scriptPath, string runnerPath, string zipPath, bool includeRunner)
        {
            return Pack(scriptPath, runnerPath, zipPath, includeRunner, true);
        }

        /// <summary>silentRun=true 时「双击运行.cmd」静默执行，不弹控制台窗口。</summary>
        public static PackResult Pack(string scriptPath, string runnerPath, string zipPath, bool includeRunner, bool silentRun)
        {
            if (!File.Exists(scriptPath)) throw new FileNotFoundException("脚本不存在：" + scriptPath);
            ScriptDocument doc = ScriptDocument.Load(scriptPath);
            List<string> errors = doc.Validate();
            if (errors.Count > 0)
                throw new InvalidDataException("脚本结构校验未通过：" + string.Join("; ", errors.ToArray()));

            string scriptDir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
            string scriptName = Path.GetFileName(scriptPath);
            string baseName = Path.GetFileNameWithoutExtension(scriptPath);
            if (string.IsNullOrEmpty(zipPath))
                zipPath = Path.Combine(scriptDir, baseName + "-打包.zip");
            zipPath = Path.GetFullPath(zipPath);

            PackResult result = new PackResult();
            result.ZipPath = zipPath;
            List<string> templates = CollectTemplates(doc);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            string stageRoot = Path.Combine(Path.GetTempPath(), "BlockAutoPack-" + Guid.NewGuid().ToString("N"));
            string stageDir = Path.Combine(stageRoot, baseName);
            Directory.CreateDirectory(stageDir);

            try
            {
                File.Copy(scriptPath, Path.Combine(stageDir, scriptName), true);
                foreach (string rel in templates)
                {
                    string src = Path.Combine(scriptDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(src))
                    {
                        result.Warnings.Add("模板缺失：" + rel);
                        continue;
                    }
                    string dst = Path.Combine(stageDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                    result.TemplateCount++;
                }

                if (includeRunner)
                {
                    if (string.IsNullOrEmpty(runnerPath) || !File.Exists(runnerPath))
                    {
                        throw new InvalidDataException("找不到 BlockRunner.exe（" + (runnerPath ?? "未指定") +
                            "）。打包必须带上运行器，否则接收方双击无法运行；请确认程序目录里有 BlockRunner.exe。");
                    }
                    else
                    {
                        File.Copy(runnerPath, Path.Combine(stageDir, "BlockRunner.exe"), true);
                    }
                }

                string readme = BuildReadme(doc, scriptName, result.TemplateCount, includeRunner);
                File.WriteAllText(Path.Combine(stageDir, "使用说明.txt"), readme, new UTF8Encoding(true));

                string head = "@echo off\r\n"
                    + "chcp 65001 >nul\r\n"
                    + "cd /d \"%~dp0\"\r\n"
                    + "if not exist BlockRunner.exe (\r\n"
                    + "  echo 未找到 BlockRunner.exe：请把本压缩包完整解压，别只拷 .block。\r\n"
                    + "  pause\r\n"
                    + "  exit /b 2\r\n"
                    + ")\r\n";
                string silentCmd = head
                    + "BlockRunner.exe run \"" + scriptName + "\" --silent %*\r\n"
                    + "exit /b %ERRORLEVEL%\r\n";
                string normalCmd = head
                    + "BlockRunner.exe run \"" + scriptName + "\" %*\r\n"
                    + "echo.\r\n"
                    + "echo 退出码：%ERRORLEVEL%\r\n"
                    + "pause\r\n";
                // 默认入口静默（不弹黑窗、结束不逗留）；想排查问题再点“查看日志运行”
                File.WriteAllText(Path.Combine(stageDir, "双击运行.cmd"),
                    silentRun ? silentCmd : normalCmd, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(stageDir, "查看日志运行.cmd"), normalCmd, new UTF8Encoding(false));

                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(stageDir, zipPath, CompressionLevel.Optimal, false);
                result.SizeBytes = new FileInfo(zipPath).Length;
                return result;
            }
            finally
            {
                try { if (Directory.Exists(stageRoot)) Directory.Delete(stageRoot, true); } catch { }
            }
        }

        private static string BuildReadme(ScriptDocument doc, string scriptName, int templateCount, bool hasRunner)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("BlockAuto 脚本包");
            sb.AppendLine("=================");
            sb.AppendLine("BlockAuto 版本：" + EngineVersion.Full);
            sb.AppendLine();
            sb.AppendLine("脚本名称：" + doc.Name);
            if (!string.IsNullOrEmpty(doc.Description)) sb.AppendLine("说明：" + doc.Description);
            sb.AppendLine("脚本文件：" + scriptName);
            sb.AppendLine("模板数量：" + templateCount);
            sb.AppendLine("包含运行器：" + (hasRunner ? "是" : "否"));
            sb.AppendLine("打包时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("使用方法：");
            sb.AppendLine("1. 请把整个压缩包解压到任意目录（.block 必须和 BlockRunner.exe 放在一起，单独拷脚本跑不了）；");
            sb.AppendLine("2. 双击「双击运行.cmd」：静默运行，不弹黑窗口；");
            sb.AppendLine("   想看过程/报错就双击「查看日志运行.cmd」，会显示日志并在结束时停留；");
            sb.AppendLine("3. 命令行等价写法：");
            sb.AppendLine("   BlockRunner.exe run " + scriptName + " --silent");
            sb.AppendLine();
            sb.AppendLine("可选参数：");
            sb.AppendLine("   --threshold 0.8   覆盖识图阈值");
            sb.AppendLine("   --log log.txt     写日志到文件");
            sb.AppendLine("   --silent          静默运行（无窗口）");
            sb.AppendLine();
            sb.AppendLine("运行中按 ESC 可紧急停止。退出码：0 成功 / 1 业务异常 / 2 结构错误 / 3 人工停止。");
            return sb.ToString();
        }
    }
}