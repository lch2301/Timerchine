package io.github.lch2301.timerchine;

import static org.junit.Assert.*;
import android.content.Context;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import org.json.JSONObject;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;

/** Compatibility and media-source boundary tests matching the desktop 1.2.0 policy. */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35)
public final class ExternalMediaPolicyTest {
    private static JSONObject publicMedia() throws Exception {
        return new JSONObject("{\"title\":\"A public item\",\"protocol\":\"https\",\"url\":\"https://cdn.example.org/file?token=abc\",\"acodec\":\"opus\",\"availability\":null,\"has_drm\":false}");
    }

    @Test public void acceptsWebPagesWithoutFileExtensionsAndHttpAsOnDesktop() {
        assertTrue(ExternalMedia.isLink("https://www.youtube.com/watch?v=public"));
        assertTrue(ExternalMedia.isLink("http://example.org/item/123"));
        assertFalse(ExternalMedia.isLink("https://user:secret@example.org/audio"));
        assertFalse(ExternalMedia.isLink("file:///data/private"));
        assertFalse(ExternalMedia.isLink("https:///missing-host"));
    }

    @Test public void allowsUnknownAvailabilityButPinsOnlyOneApprovedStream() throws Exception {
        JSONObject media=publicMedia();
        media.put("formats",new org.json.JSONArray("[{\"url\":\"https://other.example.org/alternative\"}]"));
        media.put("requested_downloads",new org.json.JSONArray("[{\"url\":\"https://cdn.example.org/file\"}]"));
        JSONObject pinned=ExternalMedia.validateAndPin(media);
        assertEquals("https://cdn.example.org/file?token=abc",pinned.getString("url"));
        assertFalse(pinned.has("formats"));
        assertFalse(pinned.has("requested_downloads"));
        assertTrue(media.has("formats"));
    }

    @Test public void rejectsRestrictedLiveDrmAndFragmentedSources() throws Exception {
        for(String mutation:new String[]{"{\"availability\":\"premium_only\"}","{\"age_limit\":18}","{\"is_live\":true}","{\"has_drm\":\"maybe\"}","{\"protocol\":\"m3u8_native\"}","{\"fragments\":[{}]}","{\"_type\":\"playlist\"}"}){
            JSONObject media=publicMedia(),changes=new JSONObject(mutation);
            for(java.util.Iterator<String> keys=changes.keys();keys.hasNext();){String key=keys.next();media.put(key,changes.get(key));}
            try{ExternalMedia.validateAndPin(media);fail("Expected restricted source rejection: "+mutation);}catch(java.io.IOException expected){assertFalse(expected.getMessage().isEmpty());}
        }
    }

    @Test public void cacheRequiresMatchingPolicyAndFilenameInsideItsOwnFolder() throws Exception {
        Context context=RuntimeEnvironment.getApplication();String url="https://example.org/public/item";
        File directory=new File(ExternalMedia.fileLocation(context,url));assertTrue(directory.mkdirs()||directory.isDirectory());
        Files.write(new File(directory,"audio.opus").toPath(),new byte[]{1,2,3});
        JSONObject metadata=new JSONObject().put("fileName","audio.opus").put("title","Prepared audio").put("durationSeconds",12).put("policyRevision",1);
        File manifest=new File(directory,"source.json");
        Files.write(manifest.toPath(),metadata.toString().getBytes(StandardCharsets.UTF_8));
        assertNotNull(ExternalMedia.getPrepared(context,url));
        metadata.put("fileName","../audio.opus");Files.write(manifest.toPath(),metadata.toString().getBytes(StandardCharsets.UTF_8));assertNull(ExternalMedia.getPrepared(context,url));
        metadata.put("fileName","audio.opus").put("policyRevision",0);Files.write(manifest.toPath(),metadata.toString().getBytes(StandardCharsets.UTF_8));assertNull(ExternalMedia.getPrepared(context,url));
    }
}
