import{readFile as h}from"node:fs/promises";import{existsSync as E}from"node:fs";import{Socket as T}from"node:net";import S from"node:path";const w="localhost",y=25916,U=2e3,p=2e3,m=1e4,P=`
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

AssetDatabase.Refresh();
var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("Cannot resolve project root from Application.dataPath.");
}

var beforeSln = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
var beforeCsproj = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
Debug.Log($"[codely-unity-lsp-server] Before generation: sln={beforeSln.Length}, csproj={beforeCsproj.Length}");

var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
var vsAssembly = loadedAssemblies.FirstOrDefault(a =>
    a.GetName().Name == "Microsoft.Unity.VisualStudio.Editor" ||
    a.GetType("Microsoft.Unity.VisualStudio.Editor.ProjectGeneration", false) != null ||
    a.GetType("Microsoft.Unity.VisualStudio.Editor.LegacyStyleProjectGeneration", false) != null);

if (vsAssembly == null)
{
    throw new Exception("Cannot find Microsoft.Unity.VisualStudio.Editor assembly. Ensure com.unity.ide.visualstudio is installed and compiled.");
}

var generatorType =
    vsAssembly.GetType("Microsoft.Unity.VisualStudio.Editor.LegacyStyleProjectGeneration", false) ??
    vsAssembly.GetType("Microsoft.Unity.VisualStudio.Editor.ProjectGeneration", false);

if (generatorType == null)
{
    throw new Exception("Cannot find LegacyStyleProjectGeneration or ProjectGeneration type.");
}

var generator = Activator.CreateInstance(generatorType, nonPublic: true);
if (generator == null)
{
    throw new Exception("Generator instance is null.");
}

var syncMethod = generatorType.GetMethod("Sync", BindingFlags.Instance | BindingFlags.Public);
if (syncMethod == null)
{
    throw new Exception("Sync() not found on Unity project generator.");
}

syncMethod.Invoke(generator, null);
AssetDatabase.Refresh();

var afterSln = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
var afterCsproj = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
Debug.Log($"[codely-unity-lsp-server] After generation: sln={afterSln.Length}, csproj={afterCsproj.Length}");
`;async function C(e){const r=S.join(e,".com-unity-codely.json");if(!E(r))return null;try{const t=await h(r,"utf8");return JSON.parse(t)}catch{return null}}function b(e){return typeof e=="number"&&Number.isInteger(e)&&e>0&&e<=65535?e:null}function O(e,r){return new Promise((t,o)=>{let i=Buffer.alloc(0),s=null;const a=()=>{clearTimeout(g),e.off("data",f),e.off("error",d),e.off("close",u),e.off("end",u)},n=c=>{a(),t(c)},l=c=>{a(),o(c)},f=c=>{i=Buffer.concat([i,c]),s===null&&i.length>=8&&(s=Number(i.readBigUInt64BE(0))),s!==null&&i.length>=8+s&&n(i.subarray(8,8+s))},d=c=>l(c),u=()=>l(new Error("Connection closed before receiving full response")),g=setTimeout(()=>{l(new Error(`Timed out waiting for Unity TCP response after ${r}ms`))},r);e.on("data",f),e.once("error",d),e.once("close",u),e.once("end",u)})}async function D(e,r){const t=new T;t.setNoDelay(!0),await new Promise((i,s)=>{const a=setTimeout(()=>{s(new Error(`Connection timeout to ${e}:${r}`))},U);t.connect(r,e,()=>{clearTimeout(a),i()}),t.once("error",n=>{clearTimeout(a),s(n)})}),t.setTimeout(m);const o=(await j(t)).toString("ascii").trim();if(!o.includes("WELCOME UNITY-TCP")||!o.includes("FRAMING=1"))throw t.destroy(),new Error(`Unexpected Unity TCP handshake from ${e}:${r}: ${o}`);return t}function j(e){return new Promise((r,t)=>{let o=Buffer.alloc(0);const i=()=>{clearTimeout(l),e.off("data",s),e.off("error",a),e.off("close",n),e.off("end",n)},s=f=>{o=Buffer.concat([o,f]),(o.includes(10)||o.length>=512)&&(i(),r(o))},a=f=>{i(),t(f)},n=()=>{i(),t(new Error("Unity TCP connection closed during handshake"))},l=setTimeout(()=>{i(),t(new Error(`Timed out waiting for Unity TCP handshake after ${p}ms`))},p);e.on("data",s),e.once("error",a),e.once("close",n),e.once("end",n)})}async function G(e,r,t){const o=await D(e,r);try{const i=Buffer.from(JSON.stringify(t),"utf8"),s=Buffer.allocUnsafe(8);s.writeBigUInt64BE(BigInt(i.length),0),o.write(s),o.write(i);const a=await O(o,m),n=JSON.parse(a.toString("utf8"));if(n.success===!1||n.status==="error")throw new Error(n.error||n.message||"Unity TCP command failed");return n.result??n}finally{o.destroy()}}async function _(e){const r=await C(e),t=b(r?.unity_port),o=typeof r?.unity_host=="string"&&r.unity_host.trim()?r.unity_host.trim():w,i=t===null?[y]:[t,y];let s="Unity TCP connection unavailable";for(const a of[...new Set(i)])try{const n=await G(o,a,{type:"execute_csharp_script",params:{script:P,capture_logs:!0}});return{ok:!0,host:o,port:a,response:n}}catch(n){s=n instanceof Error?n.message:String(n)}return{ok:!1,host:o,port:t??y,error:s}}export{_ as generateSolutionViaConnectedEditor};
