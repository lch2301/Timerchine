package io.github.lch2301.timerchine;

import android.content.Context;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.MediaMetadataRetriever;
import android.net.Uri;
import android.provider.OpenableColumns;
import com.yausername.youtubedl_android.YoutubeDL;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;

/** The desktop inspect -> validate/pin -> download/cache pipeline, using native Android yt-dlp. */
public final class ExternalMedia {
    private static final int POLICY_REVISION = 1;
    private static final String MARKER = "__TIMERCHINE_PROGRESS__";
    private static final String FORMAT = "bestaudio[protocol=https]/bestaudio[protocol=http]/best[protocol=https]/best[protocol=http]";
    private static final Map<String, Object> LOCKS = new ConcurrentHashMap<>();
    private static volatile boolean initialized;
    private ExternalMedia() { }

    public static final class CancellationToken {
        private volatile boolean cancelled;
        private volatile Process process;
        public void cancel() {
            cancelled = true;
            Process active = process;
            if (active != null) {
                active.destroy();
                Thread reaper=new Thread(()->{try{if(!active.waitFor(1,TimeUnit.SECONDS))active.destroyForcibly();}catch(InterruptedException interrupted){Thread.currentThread().interrupt();active.destroyForcibly();}},"Timerchine-media-cancel");
                reaper.setDaemon(true);reaper.start();
            }
        }
        public boolean isCancelled() { return cancelled; }
        public void check() { if (cancelled || Thread.currentThread().isInterrupted()) throw new CancellationException("작업을 취소했습니다."); }
        private void attach(Process active) { process = active; if (cancelled) { active.destroyForcibly(); check(); } }
        private void detach(Process active) { if (process == active) process = null; }
    }
    public interface ProgressListener { void onProgress(Progress progress); }
    public static final class Progress {
        public final Double percent;
        public final String status;
        public final long downloadedBytes;
        public final Long totalBytes;
        public final boolean estimated;
        Progress(Double percent, String status, long downloadedBytes, Long totalBytes, boolean estimated) {
            this.percent=percent; this.status=status; this.downloadedBytes=downloadedBytes; this.totalBytes=totalBytes; this.estimated=estimated;
        }
    }
    public static final class Prepared {
        public final String path, title;
        public final double durationSeconds;
        Prepared(String path, String title, double duration) { this.path=path; this.title=title; this.durationSeconds=duration; }
    }
    public static final class Preview {
        public final String title, thumbnailPath;
        Preview(String title, String thumbnailPath) { this.title=title; this.thumbnailPath=thumbnailPath; }
    }

    public static boolean isLink(String value) {
        try {
            Uri uri=Uri.parse(value == null ? "" : value.trim());
            String scheme=uri.getScheme();
            return ("http".equalsIgnoreCase(scheme) || "https".equalsIgnoreCase(scheme))
                    && uri.getHost()!=null && !uri.getHost().isEmpty() && (uri.getUserInfo()==null || uri.getUserInfo().isEmpty());
        } catch (RuntimeException bad) { return false; }
    }
    private static void requireConfirmed(Context context, String url) throws IOException {
        if (!isLink(url)) throw new IOException("http 또는 https로 시작하는 링크를 입력하세요.");
        if (!AppStore.load(context).externalLinksConfirmed) throw new IOException("외부 링크의 다운로드·개인 알람 이용 권한과 제공 사이트의 이용 조건을 확인하세요.");
    }
    private static File folder(Context context, String url) { return new File(new File(context.getFilesDir(), "OnlineAudio"), hash(url)); }
    public static String fileLocation(Context context, String url) { return folder(context, url).getAbsolutePath(); }
    public static Prepared getPrepared(Context context, String url) {
        if (!isLink(url)) return null;
        try {
            File directory=folder(context,url);
            JSONObject meta=readJson(new File(directory,"source.json"));
            if (meta.optInt("policyRevision")!=POLICY_REVISION) return null;
            String name=meta.optString("fileName");
            if (!name.startsWith("audio.") || !new File(name).getName().equals(name)) return null;
            File audio=new File(directory,name);
            if (!audio.isFile() || audio.length()==0) return null;
            return new Prepared(audio.getAbsolutePath(),cleanTitle(meta.optString("title")),Math.max(0,meta.optDouble("durationSeconds",0)));
        } catch (Exception missing) { return null; }
    }

    public static Preview preview(Context context, String url, CancellationToken cancellation) throws Exception {
        requireConfirmed(context,url); cancellation.check();
        synchronized (LOCKS.computeIfAbsent(hash(url), ignored -> new Object())) {
            cancellation.check(); File directory=folder(context,url); mkdir(directory);
            JSONObject details;
            try { details=readJson(new File(directory,"details.json")); }
            catch (Exception missing) {
                JSONObject inspected=inspect(context,url,cancellation);
                validateAndPin(inspected);
                details=details(inspected); writeJson(new File(directory,"details.json"),details);
            }
            String thumbnail=details.optString("thumbnailUrl");
            String thumbnailPath="";
            if (isLink(thumbnail)) {
                File target=new File(new File(context.getFilesDir(),"Thumbnails"),hash("link:"+url)+".png");
                if (!target.isFile()) {
                    try { downloadThumbnail(thumbnail,target,cancellation); }
                    catch (CancellationException cancelled) { throw cancelled; }
                    catch (Exception unavailable) { /* The title and audio remain usable without a thumbnail. */ }
                }
                if (target.isFile()) thumbnailPath=target.getAbsolutePath();
            }
            return new Preview(cleanTitle(details.optString("title")),thumbnailPath);
        }
    }

    public static Prepared prepare(Context context, String url, CancellationToken cancellation, ProgressListener progress) throws Exception {
        requireConfirmed(context,url); cancellation.check(); emit(progress,null,"다운로드 준비 중",0,null,false);
        synchronized (LOCKS.computeIfAbsent(hash(url), ignored -> new Object())) {
            cancellation.check(); Prepared cached=getPrepared(context,url);
            if(cached!=null){emit(progress,100d,"다운로드 완료",0,null,false);return cached;}
            File directory=folder(context,url);mkdir(directory);
            emit(progress,null,"접근 제한·오디오 형식 확인 중",0,null,false);
            JSONObject inspected=inspect(context,url,cancellation);
            JSONObject pinned=validateAndPin(inspected);
            JSONObject detail=details(inspected);writeJson(new File(directory,"details.json"),detail);
            File snapshot=new File(directory,".source-"+UUID.randomUUID()+".json");
            String output;
            try {
                writeJson(snapshot,pinned);
                List<String> args=extractorArguments(context);
                Collections.addAll(args,"--progress","--newline","--progress-delta","0.2","--progress-template",
                        "download:"+MARKER+"{\"status\":%(progress.status|null)j,\"downloaded_bytes\":%(progress.downloaded_bytes|null)j,\"total_bytes\":%(progress.total_bytes|null)j,\"total_bytes_estimate\":%(progress.total_bytes_estimate|null)j}",
                        "--paths",directory.getAbsolutePath(),"--output","audio.%(ext)s","--print","after_move:filepath","--no-simulate","--load-info-json",snapshot.getAbsolutePath());
                output=run(context,args,directory,cancellation,progress,false);
            } finally { snapshot.delete(); }
            String[] lines=output.trim().split("\\r?\\n");
            String reported=lines.length==0?"":lines[lines.length-1].trim();
            File audio=new File(reported);
            if(!audio.isAbsolute())audio=new File(directory,reported);
            audio=audio.getCanonicalFile();
            if(!directory.getCanonicalFile().equals(audio.getParentFile()) || !audio.getName().startsWith("audio.") || !audio.isFile() || audio.length()==0)
                throw new IOException("링크에서 재생할 오디오를 찾을 수 없습니다.");
            emit(progress,100d,"오디오 확인 중",audio.length(),audio.length(),false);
            validateAudio(context,audio,cancellation);
            double duration=probeDuration(context,audio,cancellation);
            cancellation.check();
            JSONObject metadata=new JSONObject().put("fileName",audio.getName()).put("durationSeconds",duration)
                    .put("title",detail.optString("title")).put("policyRevision",POLICY_REVISION).put("preparedAt",System.currentTimeMillis());
            writeJson(new File(directory,"source.json"),metadata);
            emit(progress,100d,"다운로드 완료",audio.length(),audio.length(),false);
            return new Prepared(audio.getAbsolutePath(),cleanTitle(detail.optString("title")),duration);
        }
    }

    /** Decode unsupported local/cached media with the bundled FFmpeg instead of dropping codecs. */
    public static Prepared prepareLocalUri(Context context, String source, CancellationToken cancellation, ProgressListener progress) throws Exception {
        cancellation.check();initialize(context);
        Uri uri=Uri.parse(source);String identity=localIdentity(context,source);String title="오디오 파일";
        if("content".equals(uri.getScheme())) {
            try(Cursor cursor=context.getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE},null,null,null)) {
                if(cursor!=null && cursor.moveToFirst()){title=cursor.getString(0);identity+="|"+cursor.getLong(1);}
            }catch(RuntimeException ignored){}
        }
        File original=null;
        if("file".equals(uri.getScheme()) || uri.getScheme()==null) {
            original=new File(uri.getScheme()==null?source:uri.getPath()).getCanonicalFile();
            if(!within(original,context.getFilesDir()) && !within(original,context.getCacheDir()))throw new IOException("앱이 준비한 파일 경로만 사용할 수 있습니다.");
            identity+="|"+original.length()+"|"+original.lastModified();title=original.getName();
        }else if(!"content".equals(uri.getScheme()))throw new IOException("기기에서 선택한 미디어 파일을 사용하세요.");
        final String key=hash(identity);
        synchronized(LOCKS.computeIfAbsent("local:"+key,ignored->new Object())) {
            File directory=new File(new File(context.getFilesDir(),"DecodedAudio"),key);mkdir(directory);
            File target=new File(directory,"audio.wav");
            if(target.isFile() && target.length()>44)return new Prepared(target.getAbsolutePath(),title,probeDuration(context,target,cancellation));
            File copied=new File(directory,".input.media"),temporary=new File(directory,".decoded.wav");
            try {
                emit(progress,null,"오디오 형식 준비 중",0,null,false);
                if(original==null){try(InputStream input=context.getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(copied)){if(input==null)throw new IOException("미디어 파일을 열지 못했습니다.");copy(input,out,cancellation,Long.MAX_VALUE);}original=copied;}
                List<String> command=new ArrayList<>();command.add(nativeExecutable(context,"libffmpeg.so"));
                Collections.addAll(command,"-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe,crypto,data","-i",original.getAbsolutePath(),"-map","0:a:0","-vn","-sn","-dn","-threads","1","-ac","2","-ar","48000","-c:a","pcm_s16le","-f","wav","-y",temporary.getAbsolutePath());
                run(context,command,directory,cancellation,null,false);
                cancellation.check();if(!temporary.isFile()||temporary.length()<=44)throw new IOException("재생할 오디오가 없습니다.");
                atomicMove(temporary,target);
                emit(progress,100d,"오디오 준비 완료",target.length(),target.length(),false);
                return new Prepared(target.getAbsolutePath(),title,probeDuration(context,target,cancellation));
            }finally{copied.delete();temporary.delete();}
        }
    }

    public static Preview localPreview(Context context, String source, CancellationToken cancellation) throws Exception {
        cancellation.check();String title="";File target=new File(new File(context.getFilesDir(),"Thumbnails"),hash("file:"+localIdentity(context,source))+".png");
        if(target.isFile())return new Preview(title,target.getAbsolutePath());
        MediaMetadataRetriever retriever=new MediaMetadataRetriever();Bitmap bitmap=null;
        try {
            Uri uri=Uri.parse(source);
            if("content".equals(uri.getScheme()))retriever.setDataSource(context,uri);else retriever.setDataSource("file".equals(uri.getScheme())?uri.getPath():source);
            title=cleanTitle(retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_TITLE));
            byte[] artwork=retriever.getEmbeddedPicture();
            if(artwork!=null && artwork.length<=16*1024*1024)bitmap=BitmapFactory.decodeByteArray(artwork,0,artwork.length);
            if(bitmap==null)bitmap=retriever.getFrameAtTime(0,MediaMetadataRetriever.OPTION_CLOSEST_SYNC);
            cancellation.check();if(bitmap!=null)writeBitmap(bitmap,target);
        }catch(CancellationException cancelled){throw cancelled;}catch(RuntimeException unavailable){}finally{retriever.release();if(bitmap!=null)bitmap.recycle();}
        if(!target.isFile()){
            File copied=null;
            try{
                initialize(context);Uri uri=Uri.parse(source);File input;
                if("content".equals(uri.getScheme())){
                    copied=new File(context.getCacheDir(),".thumbnail-"+UUID.randomUUID()+".media");
                    try(InputStream stream=context.getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(copied)){
                        if(stream==null)throw new IOException("미디어 파일을 열지 못했습니다.");copy(stream,out,cancellation,Long.MAX_VALUE);
                    }input=copied;
                }else input=new File("file".equals(uri.getScheme())?uri.getPath():source);
                mkdir(target.getParentFile());File temporary=new File(target.getParentFile(),".thumbnail-"+UUID.randomUUID()+".png");
                try{
                    List<String> args=new ArrayList<>();args.add(nativeExecutable(context,"libffmpeg.so"));
                    Collections.addAll(args,"-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe,crypto,data","-i",input.getAbsolutePath(),"-map","0:v:0","-an","-sn","-dn","-frames:v","1","-vf","scale=480:270:force_original_aspect_ratio=decrease","-threads","1","-c:v","png","-y",temporary.getAbsolutePath());
                    run(context,args,context.getCacheDir(),cancellation,null,false);
                    if(temporary.isFile()&&temporary.length()>0&&temporary.length()<4_000_000)atomicMove(temporary,target);
                }finally{temporary.delete();}
            }catch(CancellationException cancelled){throw cancelled;}catch(Exception unavailable){}finally{if(copied!=null)copied.delete();}
        }
        return new Preview(title,target.isFile()?target.getAbsolutePath():"");
    }

    private static String localIdentity(Context context,String source){
        Uri uri=Uri.parse(source);
        if("content".equals(uri.getScheme())){
            try(Cursor cursor=context.getContentResolver().query(uri,new String[]{OpenableColumns.SIZE,"last_modified"},null,null,null)){
                if(cursor!=null&&cursor.moveToFirst())return source+"|"+cursor.getLong(0)+"|"+cursor.getLong(1);
            }catch(RuntimeException ignored){}
        }else{File file=new File("file".equals(uri.getScheme())?uri.getPath():source);return source+"|"+file.length()+"|"+file.lastModified();}
        return source;
    }

    private static synchronized void initialize(Context context) throws Exception {
        if(initialized)return;
        File target=new File(new File(context.getNoBackupFilesDir(),"youtubedl-android/yt-dlp"),"yt-dlp");
        mkdir(target.getParentFile());
        File temporary=new File(target.getParentFile(),".timerchine-yt-dlp");
        try(InputStream stream=context.getAssets().open("media/yt-dlp.bin");OutputStream out=new FileOutputStream(temporary)){copy(stream,out,new CancellationToken(),32L*1024*1024);}
        String expected="b7af77f686e95f7845ce8d09d21957718febb8b48b30555adb9dd2292ad7bf14";
        byte[] checksum=MessageDigest.getInstance("SHA-256").digest(Files.readAllBytes(temporary.toPath()));
        StringBuilder actual=new StringBuilder();for(byte value:checksum)actual.append(String.format(Locale.ROOT,"%02x",value&255));
        if(!expected.contentEquals(actual)){temporary.delete();throw new IOException("외부 링크 도구의 무결성을 확인하지 못했습니다.");}
        atomicMove(temporary,target);
        File bootstrap=new File(target.getParentFile(),"extractor-bootstrap.py");
        try(InputStream in=context.getAssets().open("media/extractor-bootstrap.py");OutputStream out=new FileOutputStream(bootstrap)){copy(in,out,new CancellationToken(),1024*1024);}
        YoutubeDL.getInstance().init(context.getApplicationContext());
        initialized=true;
    }
    private static List<String> extractorArguments(Context context) throws Exception {
        initialize(context);
        List<String> args=new ArrayList<>();args.add(nativeExecutable(context,"libpython.so"));args.add(new File(context.getNoBackupFilesDir(),"youtubedl-android/yt-dlp/extractor-bootstrap.py").getAbsolutePath());args.add(new File(context.getNoBackupFilesDir(),"youtubedl-android/yt-dlp/yt-dlp").getAbsolutePath());
        Collections.addAll(args,"--ignore-config","--no-plugin-dirs","--no-remote-components","--no-cache-dir","--no-cookies","--no-cookies-from-browser","--proxy","","--geo-verification-proxy","","--no-geo-bypass","--no-allow-unplayable-formats","--no-playlist","--playlist-items","1","--match-filters","!is_live & !is_upcoming","--format",FORMAT,"--no-warnings","--no-colors","--encoding","utf-8","--socket-timeout","15","--retries","2","--fragment-retries","2","--js-runtimes","quickjs:"+nativeExecutable(context,"libqjs.so"),"--ffmpeg-location",nativeExecutable(context,"libffmpeg.so"));
        return args;
    }
    private static JSONObject inspect(Context context,String url,CancellationToken token)throws Exception{
        List<String> args=extractorArguments(context);Collections.addAll(args,"--dump-single-json","--simulate","--",url);
        return new JSONObject(run(context,args,context.getCacheDir(),token,null,true));
    }
    static JSONObject validateAndPin(JSONObject input)throws Exception{
        JSONObject info=new JSONObject(input.toString());String type=text(info,"_type"),availability=text(info,"availability"),live=text(info,"live_status");
        if(info.has("entries")&&!info.isNull("entries") || Arrays.asList("playlist","multi_video","url","url_transparent").contains(type))throw new IOException("공개된 단일 영상 또는 오디오 링크만 사용할 수 있습니다.");
        if(!availability.isEmpty() && !availability.equals("public") && !availability.equals("unlisted"))throw new IOException("로그인·구독·구매 등 접근 권한이 필요한 콘텐츠는 지원하지 않습니다.");
        if(info.optDouble("age_limit",0)>0)throw new IOException("연령 제한 콘텐츠는 지원하지 않습니다.");
        if(info.optBoolean("is_live")||info.optBoolean("is_upcoming")||Arrays.asList("is_live","is_upcoming","post_live").contains(live))throw new IOException("실시간 방송이나 아직 준비되지 않은 콘텐츠는 지원하지 않습니다.");
        Object drm=info.opt("has_drm");if(drm!=null&&drm!=JSONObject.NULL&&!Boolean.FALSE.equals(drm))throw new IOException("DRM 보호가 감지되거나 의심되는 오디오는 다운로드하지 않습니다.");
        String protocol=text(info,"protocol");
        if(length(info,"requested_formats")>0||length(info,"requested_downloads")>1||length(info,"fragments")>0||info.has("hls_aes")&&!info.isNull("hls_aes")||!(protocol.equals("http")||protocol.equals("https"))||!isLink(info.optString("url")))throw new IOException("직접 받을 수 있는 HTTP/HTTPS 오디오만 지원합니다.");
        if("none".equals(info.optString("acodec")))throw new IOException("링크에서 재생할 오디오를 찾을 수 없습니다.");
        for(String key:new String[]{"formats","requested_downloads","requested_formats","__postprocessors"})info.remove(key);
        return info;
    }
    private static int length(JSONObject object,String key){JSONArray array=object.optJSONArray(key);return array==null?0:array.length();}
    private static String text(JSONObject object,String key){Object value=object.opt(key);return value instanceof String?(String)value:"";}
    private static JSONObject details(JSONObject info)throws Exception{
        String thumbnail=text(info,"thumbnail");JSONArray images=info.optJSONArray("thumbnails");
        if(!isLink(thumbnail)&&images!=null)for(int i=images.length()-1;i>=0;i--){JSONObject image=images.optJSONObject(i);if(image!=null&&isLink(image.optString("url"))){thumbnail=image.optString("url");break;}}
        return new JSONObject().put("title",cleanTitle(info.optString("title"))).put("thumbnailUrl",isLink(thumbnail)?thumbnail:"");
    }
    private static void validateAudio(Context context,File audio,CancellationToken token)throws Exception{
        File probe=new File(audio.getParentFile(),".validation.pcm");
        try{List<String> args=new ArrayList<>();args.add(nativeExecutable(context,"libffmpeg.so"));Collections.addAll(args,"-nostdin","-hide_banner","-loglevel","error","-protocol_whitelist","file,pipe,crypto,data","-i",audio.getAbsolutePath(),"-map","0:a:0","-t","0.05","-f","s16le","-y",probe.getAbsolutePath());run(context,args,audio.getParentFile(),token,null,false);if(!probe.isFile()||probe.length()==0)throw new IOException("링크에 재생할 오디오가 없습니다.");}finally{probe.delete();}
    }
    private static double probeDuration(Context context,File audio,CancellationToken token)throws Exception{
        List<String> args=new ArrayList<>();args.add(nativeExecutable(context,"libffprobe.so"));Collections.addAll(args,"-v","error","-protocol_whitelist","file,pipe,crypto,data","-select_streams","a:0","-show_entries","stream=duration:stream_tags=DURATION:format=duration","-of","json",audio.getAbsolutePath());
        JSONObject info=new JSONObject(run(context,args,audio.getParentFile(),token,null,true));
        JSONArray streams=info.optJSONArray("streams");JSONObject stream=streams==null?null:streams.optJSONObject(0);
        double duration=stream==null?0:stream.optDouble("duration",0);if(Double.isFinite(duration)&&duration>0)return duration;
        JSONObject tags=stream==null?null:stream.optJSONObject("tags");
        if(tags!=null){String[] parts=text(tags,"DURATION").split(":");if(parts.length==3)try{duration=Double.parseDouble(parts[0])*3600+Double.parseDouble(parts[1])*60+Double.parseDouble(parts[2]);if(Double.isFinite(duration)&&duration>0)return duration;}catch(NumberFormatException ignored){}}
        JSONObject format=info.optJSONObject("format");duration=format==null?0:format.optDouble("duration",0);return Double.isFinite(duration)?Math.max(0,duration):0;
    }
    private static String nativeExecutable(Context context,String name){return new File(context.getApplicationInfo().nativeLibraryDir,name).getAbsolutePath();}
    private static String run(Context context,List<String> command,File directory,CancellationToken token,ProgressListener progress,boolean metadata)throws Exception{
        token.check();ProcessBuilder builder=new ProcessBuilder(command).directory(directory);File packages=new File(context.getNoBackupFilesDir(),"youtubedl-android/packages");
        Map<String,String> env=builder.environment();String python=new File(packages,"python/usr").getAbsolutePath();
        env.put("LD_LIBRARY_PATH",python+"/lib:"+new File(packages,"ffmpeg/usr/lib").getAbsolutePath()+":"+new File(packages,"aria2c/usr/lib").getAbsolutePath());env.put("PYTHONHOME",python);env.put("HOME",python);env.put("SSL_CERT_FILE",python+"/etc/tls/cert.pem");env.put("TMPDIR",context.getCacheDir().getAbsolutePath());env.put("PATH",System.getenv("PATH")+":"+context.getApplicationInfo().nativeLibraryDir);
        Process process=builder.start();token.attach(process);StringBuilder errors=new StringBuilder();Thread stderr=new Thread(()->{try{read(process.getErrorStream(),errors,null,token,false);}catch(Exception ignored){}},"Timerchine-media-stderr");stderr.start();StringBuilder output=new StringBuilder();
        try{read(process.getInputStream(),output,progress,token,metadata);int exit=process.waitFor();stderr.join();token.check();if(exit!=0)throw new IOException(failure(errors.toString()));return output.toString();}
        finally{
            token.detach(process);boolean interrupted=Thread.interrupted();
            if(process.isAlive()){process.destroy();try{if(!process.waitFor(1,TimeUnit.SECONDS))process.destroyForcibly();}catch(InterruptedException stopping){interrupted=true;process.destroyForcibly();}}
            try{stderr.join(1000);}catch(InterruptedException stopping){interrupted=true;}
            if(interrupted)Thread.currentThread().interrupt();
        }
    }
    private static void read(InputStream source,StringBuilder output,ProgressListener progress,CancellationToken token,boolean metadata)throws Exception{
        try(BufferedReader reader=new BufferedReader(new InputStreamReader(source,StandardCharsets.UTF_8))){String line;while((line=reader.readLine())!=null){token.check();int marker=line.indexOf(MARKER);if(!metadata&&marker>=0){try{JSONObject data=new JSONObject(line.substring(marker+MARKER.length()));long done=Math.max(0,data.optLong("downloaded_bytes",0)),total=Math.max(0,data.optLong("total_bytes",0));boolean estimate=total==0;if(estimate)total=Math.max(0,data.optLong("total_bytes_estimate",0));boolean finished="finished".equals(data.optString("status"));Double percent=null;if(finished)percent=100d;else if(total>0)percent=Math.min(100,done*100d/total);emit(progress,percent,finished?"오디오 확인 중":"다운로드 중",done,total>0?total:null,estimate&&!finished);}catch(Exception ignored){}continue;}int maximum=metadata?16*1024*1024:64*1024;if(metadata&&output.length()+(long)line.length()>maximum)throw new IOException("오디오 정보가 너무 복잡합니다. 단일 콘텐츠 링크를 입력하세요.");output.append(line).append('\n');if(!metadata&&output.length()>maximum)output.delete(0,output.length()-maximum);}}
    }
    private static String failure(String details){if(details.toLowerCase(Locale.ROOT).contains("drm"))return "DRM 보호가 감지된 콘텐츠는 지원하지 않습니다.";if(details.contains("Unsupported URL"))return "지원하지 않는 링크입니다. 영상이나 오디오가 있는 페이지의 주소를 입력하세요.";return "링크 오디오를 준비하지 못했습니다. 인터넷 연결과 링크를 확인하세요. 로그인·연령·지역 제한 콘텐츠와 실시간 방송은 지원하지 않습니다.";}
    private static void emit(ProgressListener listener,Double percent,String status,long done,Long total,boolean estimated){if(listener!=null)listener.onProgress(new Progress(percent,status,done,total,estimated));}
    private static String cleanTitle(String value){String title=value==null?"":value.replaceAll("\\s+"," ").trim();return title.length()>500?title.substring(0,500):title;}
    private static String hash(String input){try{byte[] digest=MessageDigest.getInstance("SHA-256").digest(input.getBytes(StandardCharsets.UTF_8));StringBuilder result=new StringBuilder();for(byte item:digest)result.append(String.format(Locale.ROOT,"%02X",item&255));return result.toString();}catch(Exception impossible){throw new IllegalStateException(impossible);}}
    private static JSONObject readJson(File file)throws Exception{return new JSONObject(new String(Files.readAllBytes(file.toPath()),StandardCharsets.UTF_8));}
    private static void writeJson(File file,JSONObject value)throws Exception{mkdir(file.getParentFile());File temporary=new File(file.getParentFile(),".meta-"+UUID.randomUUID());try{Files.write(temporary.toPath(),value.toString().getBytes(StandardCharsets.UTF_8));atomicMove(temporary,file);}finally{temporary.delete();}}
    private static void atomicMove(File source,File target)throws IOException{Files.move(source.toPath(),target.toPath(),StandardCopyOption.REPLACE_EXISTING);}
    private static void mkdir(File directory)throws IOException{if(!directory.isDirectory()&&!directory.mkdirs())throw new IOException("미디어 저장 폴더를 만들지 못했습니다.");}
    private static boolean within(File path,File root)throws IOException{return path.toPath().startsWith(root.getCanonicalFile().toPath());}
    private static void copy(InputStream in,OutputStream out,CancellationToken token,long maximum)throws IOException{byte[] buffer=new byte[65536];long total=0;int count;while((count=in.read(buffer))!=-1){token.check();total+=count;if(total>maximum)throw new IOException("파일이 허용된 크기를 초과했습니다.");out.write(buffer,0,count);}}
    private static void downloadThumbnail(String source,File target,CancellationToken token)throws Exception{
        HttpURLConnection connection=(HttpURLConnection)new URL(source).openConnection();connection.setConnectTimeout(15000);connection.setReadTimeout(15000);connection.setInstanceFollowRedirects(true);File temporary=new File(target.getParentFile(),".image-"+UUID.randomUUID());mkdir(target.getParentFile());
        try{if(connection.getContentLengthLong()>16*1024*1024)throw new IOException("썸네일 크기를 초과했습니다.");try(InputStream in=connection.getInputStream();OutputStream out=new FileOutputStream(temporary)){copy(in,out,token,16L*1024*1024);}BitmapFactory.Options options=new BitmapFactory.Options();options.inJustDecodeBounds=true;BitmapFactory.decodeFile(temporary.getAbsolutePath(),options);if(options.outWidth<=0||options.outHeight<=0)throw new IOException("썸네일을 읽지 못했습니다.");options.inJustDecodeBounds=false;options.inSampleSize=Math.max(1,Math.max(options.outWidth/960,options.outHeight/540));Bitmap bitmap=BitmapFactory.decodeFile(temporary.getAbsolutePath(),options);if(bitmap!=null)try{writeBitmap(bitmap,target);}finally{bitmap.recycle();}}finally{connection.disconnect();temporary.delete();}
    }
    private static void writeBitmap(Bitmap bitmap,File target)throws IOException{mkdir(target.getParentFile());float ratio=Math.min(1f,Math.min(480f/bitmap.getWidth(),270f/bitmap.getHeight()));Bitmap scaled=Bitmap.createScaledBitmap(bitmap,Math.max(1,Math.round(bitmap.getWidth()*ratio)),Math.max(1,Math.round(bitmap.getHeight()*ratio)),true);File temporary=new File(target.getParentFile(),".thumb-"+UUID.randomUUID());try(OutputStream out=new FileOutputStream(temporary)){if(!scaled.compress(Bitmap.CompressFormat.PNG,100,out))throw new IOException("썸네일을 저장하지 못했습니다.");}finally{if(scaled!=bitmap)scaled.recycle();}atomicMove(temporary,target);}
}
