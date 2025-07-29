//using Microsoft.Extensions.Configuration;
//using OpenAI.Responses;

//namespace LMSupplyDepots.External.OpenAI.SampleConsoleApp;

//class Program
//{
//    private static string ApiKey;
//    private static OpenAIService _openAI;

//    // Static constructor to load configuration
//    static Program()
//    {
//        var configuration = new ConfigurationBuilder()
//            .SetBasePath(Directory.GetCurrentDirectory())
//            .AddJsonFile("secrets.json", optional: true, reloadOnChange: true)
//            .Build();
//        ApiKey = configuration["OpenAI:ApiKey"];
//    }

//    static async Task Main(string[] args)
//    {
//        try
//        {
//            // Initialize OpenAI service with API key
//            _openAI = new OpenAIService(ApiKey);

//            bool exitRequested = false;

//            while (!exitRequested)
//            {
//                DisplayMainMenu();
//                string choice = Console.ReadLine();

//                switch (choice)
//                {
//                    case "1":
//                        await HandleFileManagementMenu();
//                        break;
//                    case "2":
//                        await HandleVectorStoreManagementMenu();
//                        break;
//                    case "3":
//                        await PerformQuery();
//                        break;
//                    case "0":
//                        exitRequested = true;
//                        break;
//                    default:
//                        Console.WriteLine("잘못된 선택입니다. 다시 시도하세요.");
//                        break;
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"오류 발생: {ex.Message}");
//        }
//    }

//    #region 메뉴 표시

//    private static void DisplayMainMenu()
//    {
//        Console.Clear();
//        Console.WriteLine("=== OpenAI 파일 및 벡터 스토어 관리 시스템 ===");
//        Console.WriteLine("1. 파일 관리");
//        Console.WriteLine("2. 벡터 스토어 관리");
//        Console.WriteLine("3. 쿼리 실행");
//        Console.WriteLine("0. 종료");
//        Console.Write("선택: ");
//    }

//    private static void DisplayFileManagementMenu()
//    {
//        Console.Clear();
//        Console.WriteLine("=== 파일 관리 ===");
//        Console.WriteLine("1. 파일 목록 조회");
//        Console.WriteLine("2. 파일 업로드");
//        Console.WriteLine("3. 파일 삭제");
//        Console.WriteLine("0. 메인 메뉴로 돌아가기");
//        Console.Write("선택: ");
//    }

//    private static void DisplayVectorStoreManagementMenu()
//    {
//        Console.Clear();
//        Console.WriteLine("=== 벡터 스토어 관리 ===");
//        Console.WriteLine("1. 벡터 스토어 목록 조회");
//        Console.WriteLine("2. 벡터 스토어 생성");
//        Console.WriteLine("3. 벡터 스토어 삭제");
//        Console.WriteLine("4. 벡터 스토어 파일 관리");
//        Console.WriteLine("0. 메인 메뉴로 돌아가기");
//        Console.Write("선택: ");
//    }

//    private static void DisplayVectorStoreFileManagementMenu()
//    {
//        Console.Clear();
//        Console.WriteLine("=== 벡터 스토어 파일 관리 ===");
//        Console.WriteLine("1. 벡터 스토어의 파일 목록 조회");
//        Console.WriteLine("2. 벡터 스토어에 파일 추가");
//        Console.WriteLine("3. 벡터 스토어에서 파일 제거");
//        Console.WriteLine("4. 벡터 스토어에 파일 일괄 추가");
//        Console.WriteLine("0. 이전 메뉴로 돌아가기");
//        Console.Write("선택: ");
//    }

//    #endregion

//    #region 파일 관리

//    private static async Task HandleFileManagementMenu()
//    {
//        bool backToMainMenu = false;

//        while (!backToMainMenu)
//        {
//            DisplayFileManagementMenu();
//            string choice = Console.ReadLine();

//            switch (choice)
//            {
//                case "1":
//                    await ListFiles();
//                    break;
//                case "2":
//                    await UploadFile();
//                    break;
//                case "3":
//                    await DeleteFile();
//                    break;
//                case "0":
//                    backToMainMenu = true;
//                    break;
//                default:
//                    Console.WriteLine("잘못된 선택입니다. 다시 시도하세요.");
//                    break;
//            }

//            if (!backToMainMenu)
//            {
//                Console.WriteLine("\n계속하려면 아무 키나 누르세요...");
//                Console.ReadKey();
//            }
//        }
//    }

//    private static async Task ListFiles()
//    {
//        Console.WriteLine("\n=== OpenAI에 업로드된 파일 목록 ===");

//        try
//        {
//            var files = await _openAI.File.ListFilesAsync();

//            if (files.Count == 0)
//            {
//                Console.WriteLine("업로드된 파일이 없습니다.");
//                return;
//            }

//            Console.WriteLine("ID\t\t\t\t\t파일명\t\t\t크기\t\t생성일");
//            Console.WriteLine("--------------------------------------------------------------------------------");

//            foreach (var file in files)
//            {
//                Console.WriteLine($"{file.Id}\t{file.Filename}\t\t{file.SizeInBytes} bytes\t{file.CreatedAt}");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"파일 목록 조회 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task UploadFile()
//    {
//        Console.WriteLine("\n=== 파일 업로드 ===");
//        Console.Write("업로드할 파일 경로를 입력하세요: ");
//        string filePath = Console.ReadLine();

//        if (!File.Exists(filePath))
//        {
//            Console.WriteLine("파일이 존재하지 않습니다.");
//            return;
//        }

//        try
//        {
//            var file = await _openAI.File.UploadFileAsync(filePath);
//            Console.WriteLine($"파일 '{file.Filename}'이(가) 성공적으로 업로드되었습니다. ID: {file.Id}");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"파일 업로드 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task DeleteFile()
//    {
//        Console.WriteLine("\n=== 파일 삭제 ===");

//        // 먼저 파일 목록을 표시
//        await ListFiles();

//        Console.Write("\n삭제할 파일의 ID를 입력하세요: ");
//        string fileId = Console.ReadLine();

//        try
//        {
//            bool result = await _openAI.File.DeleteFileAsync(fileId);
//            if (result)
//            {
//                Console.WriteLine($"파일 ID '{fileId}'이(가) 성공적으로 삭제되었습니다.");
//            }
//            else
//            {
//                Console.WriteLine("파일 삭제에 실패했습니다.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"파일 삭제 중 오류 발생: {ex.Message}");
//        }
//    }

//    #endregion

//    #region 벡터 스토어 관리

//    private static async Task HandleVectorStoreManagementMenu()
//    {
//        bool backToMainMenu = false;

//        while (!backToMainMenu)
//        {
//            DisplayVectorStoreManagementMenu();
//            string choice = Console.ReadLine();

//            switch (choice)
//            {
//                case "1":
//                    await ListVectorStores();
//                    break;
//                case "2":
//                    await CreateVectorStore();
//                    break;
//                case "3":
//                    await DeleteVectorStore();
//                    break;
//                case "4":
//                    await HandleVectorStoreFileManagementMenu();
//                    break;
//                case "0":
//                    backToMainMenu = true;
//                    break;
//                default:
//                    Console.WriteLine("잘못된 선택입니다. 다시 시도하세요.");
//                    break;
//            }

//            if (!backToMainMenu)
//            {
//                Console.WriteLine("\n계속하려면 아무 키나 누르세요...");
//                Console.ReadKey();
//            }
//        }
//    }

//    private static async Task ListVectorStores()
//    {
//        Console.WriteLine("\n=== 벡터 스토어 목록 ===");

//        try
//        {
//            var vectorStores = _openAI.VectorStore.ListVectorStores();

//            if (vectorStores.Count == 0)
//            {
//                Console.WriteLine("벡터 스토어가 없습니다.");
//                return;
//            }

//            Console.WriteLine("ID\t\t\t\t\t이름\t\t\t상태\t\t생성일");
//            Console.WriteLine("--------------------------------------------------------------------------------");

//            foreach (var store in vectorStores)
//            {
//                Console.WriteLine($"{store.Id}\t{store.Name}\t\t{store.Status}\t{store.CreatedAt}");
//                Console.WriteLine($"사용 바이트: {store.UsageBytes}");

//                // 파일 통계 정보 표시
//                if (store.FileCounts != null)
//                {
//                    Console.WriteLine($"파일 통계: 총 {store.FileCounts.Total}개 파일, 완료 {store.FileCounts.Completed}개, 진행 중 {store.FileCounts.InProgress}개, 실패 {store.FileCounts.Failed}개");
//                }
//                else
//                {
//                    Console.WriteLine("파일 정보를 가져올 수 없습니다.");
//                }
//                Console.WriteLine();
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어 목록 조회 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task CreateVectorStore()
//    {
//        Console.WriteLine("\n=== 벡터 스토어 생성 ===");

//        // 파일 목록 표시
//        await ListFiles();

//        Console.Write("\n벡터 스토어 이름을 입력하세요: ");
//        string name = Console.ReadLine();

//        Console.WriteLine("포함할 파일 ID를 입력하세요 (여러 개인 경우 쉼표로 구분):");
//        string fileIdsInput = Console.ReadLine();
//        string[] fileIds = fileIdsInput.Split(',', StringSplitOptions.RemoveEmptyEntries);

//        if (fileIds.Length == 0)
//        {
//            Console.WriteLine("최소 하나 이상의 파일 ID가 필요합니다.");
//            return;
//        }

//        try
//        {
//            // 벡터 스토어 생성
//            var vectorStore = await _openAI.VectorStore.CreateVectorStoreAsync(fileIds, name);
//            Console.WriteLine($"벡터 스토어가 생성되었습니다. ID: {vectorStore.Id}, 상태: {vectorStore.Status}");

//            // 처리 완료 대기
//            Console.WriteLine("벡터 스토어 처리가 완료될 때까지 기다리는 중...");
//            vectorStore = await _openAI.VectorStore.WaitForVectorStoreProcessingAsync(vectorStore.Id);
//            Console.WriteLine($"벡터 스토어 처리가 완료되었습니다. 상태: {vectorStore.Status}");

//            // 파일 통계 정보 표시
//            if (vectorStore.FileCounts != null)
//            {
//                Console.WriteLine($"파일 통계: 총 {vectorStore.FileCounts.Total}개 파일, 완료 {vectorStore.FileCounts.Completed}개, 진행 중 {vectorStore.FileCounts.InProgress}개, 실패 {vectorStore.FileCounts.Failed}개");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어 생성 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task DeleteVectorStore()
//    {
//        Console.WriteLine("\n=== 벡터 스토어 삭제 ===");

//        // 벡터 스토어 목록 표시
//        await ListVectorStores();

//        Console.Write("\n삭제할 벡터 스토어의 ID를 입력하세요: ");
//        string vectorStoreId = Console.ReadLine();

//        try
//        {
//            bool result = await _openAI.VectorStore.DeleteVectorStoreAsync(vectorStoreId);
//            if (result)
//            {
//                Console.WriteLine($"벡터 스토어 ID '{vectorStoreId}'이(가) 성공적으로 삭제되었습니다.");
//            }
//            else
//            {
//                Console.WriteLine("벡터 스토어 삭제에 실패했습니다.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어 삭제 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task HandleVectorStoreFileManagementMenu()
//    {
//        // 벡터 스토어 목록 표시
//        await ListVectorStores();

//        Console.Write("\n작업할 벡터 스토어의 ID를 입력하세요: ");
//        string vectorStoreId = Console.ReadLine();

//        // 입력된 벡터 스토어 ID 유효성 확인
//        try
//        {
//            var vectorStores = _openAI.VectorStore.ListVectorStores();
//            bool isValidVectorStore = vectorStores.Any(vs => vs.Id == vectorStoreId);

//            if (!isValidVectorStore)
//            {
//                Console.WriteLine("해당 ID의 벡터 스토어가 존재하지 않습니다.");
//                return;
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어 확인 중 오류 발생: {ex.Message}");
//            return;
//        }

//        bool backToPreviousMenu = false;

//        while (!backToPreviousMenu)
//        {
//            DisplayVectorStoreFileManagementMenu();
//            string choice = Console.ReadLine();

//            switch (choice)
//            {
//                case "1":
//                    await ListVectorStoreFiles(vectorStoreId);
//                    break;
//                case "2":
//                    await AddFileToVectorStore(vectorStoreId);
//                    break;
//                case "3":
//                    await RemoveFileFromVectorStore(vectorStoreId);
//                    break;
//                case "4":
//                    await AddFilesToVectorStore(vectorStoreId);
//                    break;
//                case "0":
//                    backToPreviousMenu = true;
//                    break;
//                default:
//                    Console.WriteLine("잘못된 선택입니다. 다시 시도하세요.");
//                    break;
//            }

//            if (!backToPreviousMenu)
//            {
//                Console.WriteLine("\n계속하려면 아무 키나 누르세요...");
//                Console.ReadKey();
//            }
//        }
//    }

//    private static async Task ListVectorStoreFiles(string vectorStoreId)
//    {
//        Console.WriteLine($"\n=== 벡터 스토어 '{vectorStoreId}'의 파일 목록 ===");

//        try
//        {
//            var files = _openAI.VectorStore.ListVectorStoreFiles(vectorStoreId);

//            if (files.Count == 0)
//            {
//                Console.WriteLine("벡터 스토어에 연결된 파일이 없습니다.");
//                return;
//            }

//            Console.WriteLine("파일 ID\t\t\t\t상태\t\t크기\t\t생성일");
//            Console.WriteLine("--------------------------------------------------------------------------------");

//            foreach (var file in files)
//            {
//                Console.WriteLine($"{file.FileId}\t{file.Status}\t{file.Size} bytes\t{file.CreatedAt}");
//                if (file.LastError != null)
//                {
//                    Console.WriteLine($"오류: {file.LastError.Code} - {file.LastError.Message}");
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어 파일 목록 조회 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task AddFileToVectorStore(string vectorStoreId)
//    {
//        Console.WriteLine($"\n=== 벡터 스토어 '{vectorStoreId}'에 파일 추가 ===");

//        // 사용 가능한 파일 목록 표시
//        await ListFiles();

//        Console.Write("\n추가할 파일의 ID를 입력하세요: ");
//        string fileId = Console.ReadLine();

//        try
//        {
//            var fileAssociation = await _openAI.VectorStore.AddFileToVectorStoreAsync(vectorStoreId, fileId);
//            Console.WriteLine($"파일 '{fileId}'이(가) 벡터 스토어에 추가되었습니다. 상태: {fileAssociation.Status}");

//            // 파일 상태가 completed가 아니면 추가 정보 제공
//            if (fileAssociation.Status.ToString().ToLower() != "completed")
//            {
//                Console.WriteLine("파일이 처리 중입니다. 나중에 상태를 다시 확인하세요.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어에 파일 추가 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task RemoveFileFromVectorStore(string vectorStoreId)
//    {
//        Console.WriteLine($"\n=== 벡터 스토어 '{vectorStoreId}'에서 파일 제거 ===");

//        // 벡터 스토어에 있는 파일 목록 표시
//        await ListVectorStoreFiles(vectorStoreId);

//        Console.Write("\n제거할 파일의 ID를 입력하세요: ");
//        string fileId = Console.ReadLine();

//        try
//        {
//            bool result = await _openAI.VectorStore.RemoveFileFromVectorStoreAsync(vectorStoreId, fileId);
//            if (result)
//            {
//                Console.WriteLine($"파일 '{fileId}'이(가) 벡터 스토어에서 성공적으로 제거되었습니다.");
//            }
//            else
//            {
//                Console.WriteLine("파일 제거에 실패했습니다.");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어에서 파일 제거 중 오류 발생: {ex.Message}");
//        }
//    }

//    private static async Task AddFilesToVectorStore(string vectorStoreId)
//    {
//        Console.WriteLine($"\n=== 벡터 스토어 '{vectorStoreId}'에 파일 일괄 추가 ===");

//        // 사용 가능한 파일 목록 표시
//        await ListFiles();

//        Console.WriteLine("\n추가할 파일 ID를 입력하세요 (여러 개인 경우 쉼표로 구분):");
//        string fileIdsInput = Console.ReadLine();
//        string[] fileIds = fileIdsInput.Split(',', StringSplitOptions.RemoveEmptyEntries);

//        if (fileIds.Length == 0)
//        {
//            Console.WriteLine("최소 하나 이상의 파일 ID가 필요합니다.");
//            return;
//        }

//        try
//        {
//            var batchJob = await _openAI.VectorStore.AddFilesToVectorStoreAsync(vectorStoreId, fileIds);
//            Console.WriteLine($"배치 작업이 생성되었습니다. 배치 ID: {batchJob.BatchId}, 상태: {batchJob.Status}");
//            Console.WriteLine($"총 {fileIds.Length}개 파일이 처리 중입니다. 나중에 상태를 확인하세요.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"벡터 스토어에 파일 일괄 추가 중 오류 발생: {ex.Message}");
//        }
//    }

//    #endregion

//    #region 쿼리 실행

//    private static async Task PerformQuery()
//    {
//        Console.WriteLine("\n=== 쿼리 실행 ===");

//        // 벡터 스토어 목록 표시
//        await ListVectorStores();

//        Console.Write("\n쿼리에 사용할 벡터 스토어의 ID를 입력하세요: ");
//        string vectorStoreId = Console.ReadLine();

//        Console.Write("질문을 입력하세요: ");
//        string query = Console.ReadLine();

//        try
//        {
//            var response = await _openAI.Query.QueryFilesAsync(vectorStoreId, query);

//            Console.WriteLine("\n=== 응답 ===");
//            // Get output text using extension method
//            string responseText = response.GetOutputText();
//            Console.WriteLine(responseText);

//            // Get file annotations using extension method
//            if (response.FileAnnotations() is List<ResponseMessageAnnotation> fileAnnotations && fileAnnotations.Count != 0)
//            {
//                Console.WriteLine("\n=== 인용 정보 ===");
//                foreach (var annotation in fileAnnotations)
//                {
//                    Console.WriteLine($"파일 ID: {annotation.FileCitationFileId}");
//                    Console.WriteLine($"인덱스: {annotation.FileCitationIndex}");
//                    Console.WriteLine();
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"쿼리 실행 중 오류 발생: {ex.Message}");
//        }

//        Console.WriteLine("\n계속하려면 아무 키나 누르세요...");
//        Console.ReadKey();
//    }

//    #endregion
//}