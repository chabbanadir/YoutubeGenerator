//using Microsoft.AspNetCore.Components.Forms;
//using YoutubeGenerator.Interfaces;
//using YoutubeGenerator.Models;
//using YoutubeGenerator.Services;

//namespace YoutubeGenerator.Orchestrator
//{
//    public class VideoPipeline
//    {
//        private readonly IDownloadService _download;
//        private readonly ITrimService _trimService;
//        private readonly ITransitionService _transitionService;

//        public VideoPipeline(
//            IDownloadService download,
//            ITrimService trimService,
//            ITransitionService transitionService)
//        {
//            _download = download;
//            _trimService = trimService;
//            _transitionService = transitionService;
//        }

//        public async Task<string> ExecuteAsync(
//            string jobFolder,
//            string ressourcesPath,
//            string gifPath,
//            List<ClipInfo> clips,
//            PipelineOptions opt)
//        {
//            // === PREPARE FOLDERS ===
//            var rawDir = Path.Combine(jobFolder, "raw");
//            Directory.CreateDirectory(rawDir);

//            // === PHASE 1: DOWNLOAD ONLY ===
//            var rawFiles = new List<string>();

//            if (opt.Download)
//            {
//                Console.WriteLine("[Pipeline] === PHASE 1: DOWNLOADING ===");
//                int i = 0;
//                foreach (var clip in clips.OrderBy(c => c.Index))
//                {
//                    var file = await _download.DownloadAsync(clip, rawDir, i);
//                    i++;
//                    rawFiles.Add(file);
//                    Console.WriteLine($"[Pipeline] Downloaded: {file}/{i-1}.mp4");
//                }
//            }

//            // === PHASE 2: TRIMMING ===
//            var trimFiles = new List<string>();
        

//            if (opt.Trim)
//            {
//                Console.WriteLine("[Pipeline] === PHASE 2: TRIMMING ===");
//                var trimDir = Path.Combine(jobFolder, "trim");
//                Directory.CreateDirectory(trimDir);
                
//                Console.WriteLine("[Pipeline] Intro: Processing the intro file");
//                string introPath = Path.Combine(ressourcesPath, "Intro1.mp4");
//                await _trimService.ConcatIntroWithFirstClipAsync(introPath, rawDir, trimDir);


//                foreach (var clip in clips.OrderBy(c => c.Index))
//                {
//                    var inputFile = Path.Combine(rawDir, $"{clip.Index}.mp4");

//                    if (!File.Exists(inputFile))
//                    {
//                        Console.WriteLine($"[Pipeline] Warning: Input file not found: {inputFile}");
//                        continue;
//                    }

//                    var trimmedFile = await _trimService.TrimAsync(inputFile, clip, trimDir, clip.Index);
//                    trimFiles.Add(trimmedFile.File);
//                    Console.WriteLine($"[Pipeline] Trimmed: {trimmedFile}");
//                }
//            }

//            // === PHASE 3: TRANSITIONS ===
//            string finalOutput = null;

//            if (opt.Transitions)
//            {
//                Console.WriteLine("[Pipeline] === PHASE 3: APPLYING TRANSITIONS ===");

//                // Determine which files to use
//                var inputFiles = trimFiles.Any() ? trimFiles : rawFiles;

//                if (inputFiles.Count < 2)
//                {
//                    throw new Exception("Need at least 2 videos to apply transitions");
//                }

//                // Create output directory
//                var outputDir = Path.Combine(jobFolder, "output");
//                Directory.CreateDirectory(outputDir);

//                // Build output path
//                finalOutput = Path.Combine(outputDir, $"{opt.FinalName}.mp4");

//                // Execute transition merge (ONE SHOT for all videos)
//                var success = await _transitionService.MergeVideosWithTransitionsAsync(
//                    videoPaths: inputFiles,
//                    outputPath: finalOutput,
//                    transitionType: opt.TransitionType  , // You can make this configurable
//                    transitionDuration: opt.TransitionDuration  // You can make this configurable
//                );

//                if (!success)
//                {
//                    throw new Exception("Failed to apply transitions");
//                }

//                Console.WriteLine($"[Pipeline] ✓ Final video created: {finalOutput}");
//            }
            
//            //// === PHASE 4: MONTAGE (Future) ===
//            //if (opt.Montage)
//            //{
//            //    var duration = await _transitionService.GetVideoDurationAsync(finalOutput);
//            //    await _trimService.AddGifOverlaySimpleAsync(
//            //        videoPath: finalOutput,
//            //        duration,
//            //        gifPath: gifPath,
//            //        posX: 30,
//            //        posY: 30,
//            //        scaleWidth: 200,
//            //        scaleHeight: 200);
//            //}

//            // Return final output or list of processed files
//            if (!string.IsNullOrEmpty(finalOutput))
//            {
//                return finalOutput;
//            }
//            else if (trimFiles.Any())
//            {
//                return string.Join(Environment.NewLine, trimFiles);
//            }
//            else
//            {
//                return string.Join(Environment.NewLine, rawFiles);
//            }
//        }
//    }
//}
////using YoutubeGenerator.Interfaces;
////using YoutubeGenerator.Models;

////namespace YoutubeGenerator.Orchestrator
////{
////    public class VideoPipeline
////    {
////        private readonly IDownloadService _download;
////        private readonly ITrimService _trim;
////        private readonly ITransitionService _transition;
////        private readonly IConcatService _concat;

////        public VideoPipeline(
////            IDownloadService download,
////            ITrimService trim,
////            ITransitionService transition,
////            IConcatService concat)
////        {
////            _download = download;
////            _trim = trim;
////            _transition = transition;
////            _concat = concat;
////        }

////        public async Task<string> ExecuteAsync(
////            string jobFolder,
////            List<ClipInfo> clips,
////            PipelineOptions opt)
////        {
////            // === PREPARE FOLDERS ===
////            var rawDir = Path.Combine(jobFolder, "raw");
////            var trimmedDir = Path.Combine(jobFolder, "trimmed");
////            var transitionDir = Path.Combine(jobFolder, "transitions");
////            var finalDir = Path.Combine(jobFolder, "final");

////            Directory.CreateDirectory(rawDir);
////            Directory.CreateDirectory(trimmedDir);
////            Directory.CreateDirectory(transitionDir);
////            Directory.CreateDirectory(finalDir);

////            // === Phase 1: DOWNLOAD ===
////            var rawFiles = new List<string>();
////            if (opt.Download)
////            {
////                foreach (var clip in clips.OrderBy(c => c.Index))
////                {
////                    var file = await _download.DownloadAsync(clip, rawDir);
////                    rawFiles.Add(file);
////                }
////            }
////            else
////            {
////                // if download disabled, user must give existing files
////                rawFiles = clips.OrderBy(c => c.Index)
////                                .Select(c => c.Url ?? throw new Exception("Clip missing file path"))
////                                .ToList();
////            }

////            // === Phase 2: TRIM ===
////            var trimmedFiles = new List<string>();
////            if (opt.Trim)
////            {
////                for (int i = 0; i < clips.Count; i++)
////                {
////                    var file = await _trim.TrimAsync(rawFiles[i], clips[i], trimmedDir);
////                    trimmedFiles.Add(file);
////                }
////            }
////            else
////            {
////                trimmedFiles = rawFiles;
////            }

////            // === Phase 3: TRANSITIONS ===
////            var filesAfterTransitions = new List<string>();
////            if (opt.Transitions && clips.Count > 1)
////            {
////                // No transitions before the first file
////                filesAfterTransitions.Add(trimmedFiles[0]);

////                for (int i = 1; i < trimmedFiles.Count; i++)
////                {
////                    var prev = trimmedFiles[i - 1];
////                    var next = trimmedFiles[i];

////                    var output = await _transition.ApplyTransitionAsync(
////                        prev,
////                        next,
////                        transitionDir,
////                        duration: 1.0 // default for now; you can make it customizable
////                    );

////                    filesAfterTransitions.Add(output);
////                }
////            }
////            else
////            {
////                filesAfterTransitions = trimmedFiles;
////            }

////            // === Phase 4: CONCAT (Final Output) ===
////            var finalPath = Path.Combine(finalDir, opt.FinalName + ".mp4");
////            var outputFile = await _concat.ConcatAsync(filesAfterTransitions, finalPath);

////            return outputFile;
////        }
////    }
////}
