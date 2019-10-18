/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;


namespace forgeSample.Controllers
{
    [ApiController]
    public class DesignAutomationController : ControllerBase
    {
        // �A�v���P�[�V�����t�H���_�ւ̃A�N�Z�X�Ɏg�p�i�t�@�C���ƃo���h���̈ꎞ�I�ȏꏊ�j
        private IHostingEnvironment _env;
        // SignalR�n�u�ւ̃A�N�Z�X�Ɏg�p
        private IHubContext<DesignAutomationHub> _hubContext;
        // �o���h���̃��[�J���t�H���_
        public string LocalBundlesFolder { get { return Path.Combine(_env.WebRootPath, "bundles"); } }
        /// AppBundle�ƃA�N�e�B�r�e�B�̃v���t�B�b�N�X
        public static string NickName { get { return OAuthController.GetAppSetting("FORGE_CLIENT_ID"); } }
        /// �A�v���̃G�C���A�X�i��FDEV�ASTG�APROD�j�B���̒l�͊��ϐ�����擾�����ꍇ������܂�
        public static string Alias { get { return "dev"; } }
        // Design Automation v3 API
        DesignAutomationClient _designAutomation;

        // �R���X�g���N�^�[�Benv�����hubContext���w�肳��܂�
        public DesignAutomationController(IHostingEnvironment env, IHubContext<DesignAutomationHub> hubContext, DesignAutomationClient api)
        {
            _designAutomation = api;
            _env = env;
            _hubContext = hubContext;
        }

        /// <summary>
        /// ���̃A�J�E���g�ɒ�`����Ă��邷�ׂẴA�N�e�B�r�e�B���擾����
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/activities")]
        public async Task<List<string>> GetDefinedActivities()
        {
            // �t�B���^���X�g 
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            List<string> definedActivities = new List<string>();
            foreach (string activity in activities.Data)
                if (activity.StartsWith(NickName) && activity.IndexOf("$LATEST") == -1)
                    definedActivities.Add(activity.Replace(NickName + ".", String.Empty));

            return definedActivities;
        }

        /// <summary>
        /// �V�����A�N�e�B�r�e�B�̒�`
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/activities")]
        public async Task<IActionResult> CreateActivity([FromBody]JObject activitySpecs)
        {
            // ��{���͌���
            string zipFileName = activitySpecs["zipFileName"].Value<string>();
            string engineName = activitySpecs["engine"].Value<string>();

            // ���̃T���v���̕W����
            string appBundleName = zipFileName + "AppBundle";
            string activityName = zipFileName + "Activity";

            // 
            Page<string> activities = await _designAutomation.GetActivitiesAsync();
            string qualifiedActivityId = string.Format("{0}.{1}+{2}", NickName, activityName, Alias);
            if (!activities.Data.Contains(qualifiedActivityId))
            {
                // �A�N�e�B�r�e�B���`����
                // ToDo: �قȂ�G���W���p�̃p�����^���C�Y...
                dynamic engineAttributes = EngineAttributes(engineName);
                string commandLine = string.Format(engineAttributes.commandLine, appBundleName);
                Activity activitySpec = new Activity()
                {
                    Id = activityName,
                    Appbundles = new List<string>() { string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias) },
                    CommandLine = new List<string>() { commandLine },
                    Engine = engineName,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "inputFile", new Parameter() { Description = "input file", LocalName = "$(inputFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "inputJson", new Parameter() { Description = "input json", LocalName = "params.json", Ondemand = false, Required = false, Verb = Verb.Get, Zip = false } },
                        { "outputFile", new Parameter() { Description = "output file", LocalName = "outputFile." + engineAttributes.extension, Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
                    },
                    Settings = new Dictionary<string, ISetting>()
                    {
                        { "script", new StringSetting(){ Value = engineAttributes.script } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // ���̃A�N�e�B�r�e�B�̃G�C���A�X���w�肷��
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(activityName, aliasSpec);

                return Ok(new { Activity = qualifiedActivityId });
            }

            // ���̃A�N�e�B�r�e�B��,AppBundle��"dev"�G�C���A�X(�o���h���̍Ō�̃o�[�W�������w��)���w���܂��B
            // �A�b�v�f�[�g�̕K�v�͂���܂���(���̃T���v���ł�),����͈قȂ�R���e�L�X�g�p�Ɋg���ł��܂��B
            return Ok(new { Activity = "Activity already defined" });
        }

        /// <summary>
        /// �G���W�������ʂ��܂��B
        /// </summary>
        private dynamic EngineAttributes(string engine)
        {
            if (engine.Contains("3dsMax")) return new { commandLine = @"$(engine.path)\\3dsmaxbatch.exe -sceneFile $(args[inputFile].path) $(settings[script].path)", extension = "max", script = "da = dotNetClass(\"Autodesk.Forge.Sample.DesignAutomation.Max.RuntimeExecute\")\nda.ModifyWindowWidthHeight()\n" };
            if (engine.Contains("AutoCAD")) return new { commandLine = "$(engine.path)\\accoreconsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path) /s $(settings[script].path)", extension = "dwg", script = "UpdateParam\n" };
            if (engine.Contains("Inventor")) return new { commandLine = "$(engine.path)\\InventorCoreConsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path)", extension = "ipt", script = string.Empty };
            if (engine.Contains("Revit")) return new { commandLine = "$(engine.path)\\revitcoreconsole.exe /i $(args[inputFile].path) /al $(appbundles[{0}].path)", extension = "rvt", script = string.Empty };
            throw new Exception("Invalid engine");
        }

        /// <summary>
        /// �V����appbundle���`����
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/appbundles")]
        public async Task<IActionResult> CreateAppBundle([FromBody]JObject appBundleSpecs)
        {
            // ��{���͌���
            string zipFileName = appBundleSpecs["zipFileName"].Value<string>();
            string engineName = appBundleSpecs["engine"].Value<string>();

            // ���̃T���v���̕W����
            string appBundleName = zipFileName + "AppBundle";

            // ZIP with bundle �������ɑ��݂��邩�ǂ������m�F����
            string packageZipPath = Path.Combine(LocalBundlesFolder, zipFileName + ".zip");
            if (!System.IO.File.Exists(packageZipPath)) throw new Exception("Appbundle not found at " + packageZipPath);

            // �A�v���P�[�V�����o���h�����`����
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();

            // �A�v���P�[�V�����o���h�������łɒ�`����Ă��邩�ǂ������m�F����
            dynamic newAppVersion;
            string qualifiedAppBundleId = string.Format("{0}.{1}+{2}", NickName, appBundleName, Alias);
            if (!appBundles.Data.Contains(qualifiedAppBundleId))
            {
                // appbundle(version 1)���쐬��
                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = appBundleName,
                    Engine = engineName,
                    Id = appBundleName,
                    Description = string.Format("Description for {0}", appBundleName),

                };
                newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");

                // v1���w���G�C���A�X���쐬����
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(appBundleName, aliasSpec);
            }
            else
            {
                // �V�����o�[�W�������쐬����
                AppBundle appBundleSpec = new AppBundle()
                {
                    Engine = engineName,
                    Description = appBundleName
                };
                newAppVersion = await _designAutomation.CreateAppBundleVersionAsync(appBundleName, appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new version");

                // v+1���w���X�V�G�C���A�X
                AliasPatch aliasSpec = new AliasPatch()
                {
                    Version = newAppVersion.Version
                };
                Alias newAlias = await _designAutomation.ModifyAppBundleAliasAsync(appBundleName, Alias, aliasSpec);
            }

            // .bundle��zip���A�b�v���[�h����
            RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
            RestRequest request = new RestRequest(string.Empty, Method.POST);
            request.AlwaysMultipartFormData = true;
            foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
            request.AddFile("file", packageZipPath);
            request.AddHeader("Cache-Control", "no-cache");
            await uploadClient.ExecuteTaskAsync(request);

            return Ok(new { AppBundle = qualifiedAppBundleId, Version = newAppVersion.Version });
        }

        /// <summary>
        /// StartWorkitem �̓���
        /// </summary>
        public class StartWorkitemInput
        {
            public IFormFile inputFile { get; set; }
            public string data { get; set; }
        }

        /// <summary>
        /// new workitem���J�n����
        /// </summary>
        [HttpPost]
        [Route("api/forge/designautomation/workitems")]
        public async Task<IActionResult> StartWorkitem([FromForm]StartWorkitemInput input)
        {
            // ��{���͌���
            JObject workItemData = JObject.Parse(input.data);
            //string widthParam = workItemData["width"].Value<string>();
            //string heigthParam = workItemData["height"].Value<string>();

            string elemIdStrParam  = workItemData["elemIdStr"].Value<string>();
            string commentStrParam = workItemData["commentStr"].Value<string>();
            string activityName = string.Format("{0}.{1}", NickName, workItemData["activityName"].Value<string>());
            string browerConnectionId = workItemData["browerConnectionId"].Value<string>();

            // �t�@�C�����T�[�o�[�ɕۑ�����
            var fileSavePath = Path.Combine(_env.ContentRootPath, Path.GetFileName(input.inputFile.FileName));
            using (var stream = new FileStream(fileSavePath, FileMode.Create)) await input.inputFile.CopyToAsync(stream);

            // OAuth token
            dynamic oauth = await OAuthController.GetInternalAsync();

            // upload file to OSS Bucket
            // 1. �o�P�b�g�����݂��邱�Ƃ��m�F����
            string bucketKey = NickName.ToLower() + "_designautomation";
            BucketsApi buckets = new BucketsApi();
            buckets.Configuration.AccessToken = oauth.access_token;
            try
            {
                PostBucketsPayload bucketPayload = new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
                await buckets.CreateBucketAsync(bucketPayload, "US");
            }
            catch { }; // �o�P�b�g�����łɑ��݂���ꍇ��
                       // 2. �A�b�v���[�h���̓t�@�C��
            string inputFileNameOSS = string.Format("{0}_input_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(input.inputFile.FileName)); // avoid overriding
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = oauth.access_token;
            using (StreamReader streamReader = new StreamReader(fileSavePath))
                await objects.UploadObjectAsync(bucketKey, inputFileNameOSS, (int)streamReader.BaseStream.Length, streamReader.BaseStream, "application/octet-stream");
            System.IO.File.Delete(fileSavePath);// delete server copy

            // ��ƍ��ڂ̋c�_����������
            // 1. ���̓t�@�C��
            XrefTreeArgument inputFileArgument = new XrefTreeArgument()
            {
                Url = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, inputFileNameOSS),
                Headers = new Dictionary<string, string>()
                 {
                     { "Authorization", "Bearer " + oauth.access_token }
                 }
            };
            // 2. input json
            dynamic inputJson = new JObject();
            inputJson.ElemIdStr = elemIdStrParam;
            inputJson.CommentStr = commentStrParam;

            //inputJson.ElemIdStr = widthParam;
            //inputJson.CommentStr = heigthParam;
            XrefTreeArgument inputJsonArgument = new XrefTreeArgument()
            {
                Url = "data:application/json, " + ((JObject)inputJson).ToString(Formatting.None).Replace("\"", "'")
            };
            // 3. output file
            string outputFileNameOSS = string.Format("{0}_output_{1}", DateTime.Now.ToString("yyyyMMddhhmmss"), Path.GetFileName(input.inputFile.FileName)); // avoid overriding
            XrefTreeArgument outputFileArgument = new XrefTreeArgument()
            {
                Url = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, outputFileNameOSS),
                Verb = Verb.Put,
                Headers = new Dictionary<string, string>()
                   {
                       {"Authorization", "Bearer " + oauth.access_token }
                   }
            };

            // ��ƍ��ڂ��쐬���Ē�o����
            string callbackUrl = string.Format("{0}/api/forge/callback/designautomation?id={1}&outputFileName={2}&bucketKey={3}", OAuthController.GetAppSetting("FORGE_WEBHOOK_URL"), browerConnectionId, outputFileNameOSS, bucketKey);
            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = activityName,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "inputFile", inputFileArgument },
                    { "inputJson",  inputJsonArgument },
                    { "outputFile", outputFileArgument },
                    { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemsAsync(workItemSpec);

            return Ok(new { WorkItemId = workItemStatus.Id });
        }

        /// <summary>
        /// Design Automation workitem����̃R�[���o�b�N(onProgress�܂���onComplete)
        /// </summary>
        [HttpPost]
        [Route("/api/forge/callback/designautomation")]
        public async Task<IActionResult> OnCallback(string id, string outputFileName, string bucketKey, [FromBody]dynamic body)
        {
            try
            {
                // Webhook �͂����ɖ߂�͂��ł��BHangfire���g�p���ăW���u���X�P�W���[���ł��܂��B
                JObject bodyJson = JObject.Parse((string)body.ToString());
                await _hubContext.Clients.Client(id).SendAsync("onComplete", bodyJson.ToString());

                var client = new RestClient(bodyJson["reportUrl"].Value<string>());
                var request = new RestRequest(string.Empty);

                byte[] bs = client.DownloadData(request);
                string report = System.Text.Encoding.Default.GetString(bs);
                await _hubContext.Clients.Client(id).SendAsync("onComplete", report);

                ObjectsApi objectsApi = new ObjectsApi();
                dynamic signedUrl = await objectsApi.CreateSignedResourceAsyncWithHttpInfo(NickName.ToLower() + "_designautomation", outputFileName, new PostBucketsSigned(10), "read");
                await _hubContext.Clients.Client(id).SendAsync("downloadResult", (string)(signedUrl.Data.signedUrl));
            
                dynamic objectDetail = await objectsApi.GetObjectDetailsAsyncWithHttpInfo(bucketKey, outputFileName);
		            Encoding encoding = Encoding.UTF8;
		            string objectUrn = (string)(objectDetail.Data.objectId);
		            byte[] bytes = encoding.GetBytes(objectUrn);
		            string urn = System.Convert.ToBase64String(bytes);
		            await _hubContext.Clients.Client(id).SendAsync("translateResult", urn);
            }
            catch (Exception e) { }

            // �K��OK��Ԃ� (200)
            return Ok();
        }

        /// <summary>
        /// �g�p�\�ȃG���W���̃��X�g��Ԃ�
        /// </summary>
        [HttpGet]
        [Route("api/forge/designautomation/engines")]
        public async Task<List<string>> GetAvailableEngines()
        {
            dynamic oauth = await OAuthController.GetInternalAsync();

            // define Engines API
            Page<string> engines = await _designAutomation.GetEnginesAsync();
            engines.Data.Sort();

            return engines.Data; // return list of engines
        }

        /// <summary>
        /// �A�J�E���g���N���A���܂�(�f�o�b�O�p)�B
        /// </summary>
        [HttpDelete]
        [Route("api/forge/designautomation/account")]
        public async Task<IActionResult> ClearAccount()
        {
            // clear account
            await _designAutomation.DeleteForgeAppAsync("me");
            return Ok();
        }

        /// <summary>
        /// ���̃v���W�F�N�g�ɂ�����appbandle�̖��O
        /// </summary>
        [HttpGet]
        [Route("api/appbundles")]
        public string[] GetLocalBundles()
        {
            // ���̃t�H���_�̓p�u���b�N�t�H���_�̉��ɒu����܂��B
            // �������قƂ�ǂ̃z�X�g�ŊȒP�Ɍ��J�ł���悤�ɂ��̕��@�͒�`����Ă��܂��B
            return Directory.GetFiles(LocalBundlesFolder, "*.zip").Select(Path.GetFileNameWithoutExtension).ToArray();
        }

        [HttpPost]
        [HttpPost]
        [Route("api/forge/modelderivative/job")]
        public async Task<IActionResult> StartTranslation([FromBody]JObject translateSpecs)
        {
	        string urn = translateSpecs["urn"].Value<string>();

	        dynamic oauth = await OAuthController.GetInternalAsync();

	        List<JobPayloadItem> outputs = new List<JobPayloadItem>()
	        {
		        new JobPayloadItem(
			        JobPayloadItem.TypeEnum.Svf,
			        new List<JobPayloadItem.ViewsEnum>()
			        {
			        JobPayloadItem.ViewsEnum._2d,
			        JobPayloadItem.ViewsEnum._3d
			        })
	        };
	        JobPayload job;
	        job = new JobPayload(new JobPayloadInput(urn), new JobPayloadOutput(outputs));

	        // translation���J�n����
	        DerivativesApi derivative = new DerivativesApi();
	        derivative.Configuration.AccessToken = oauth.access_token;
	        dynamic jobPosted = await derivative.TranslateAsync(job);

	        return Ok();
        }

        [HttpGet]
        [Route("api/forge/modelderivative/manifest")]
        public async Task<IActionResult> GetManifest([FromQuery]string urn)
        {
	        DerivativesApi derivative = new DerivativesApi();
	        dynamic result = await derivative.GetManifestAsyncWithHttpInfo(urn);

	        return Ok(new { Status = (string)result.Data.status, Progress = (string)result.Data.progress });
        }

    }

    /// <summary>
    /// SignalR �̃N���X�g�p
    /// </summary>
    public class DesignAutomationHub : Microsoft.AspNetCore.SignalR.Hub
    {
        public string GetConnectionId() { return Context.ConnectionId; }
    }

}