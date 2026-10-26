package io.github.lch2301.timerchine;

import android.content.Context;
import android.view.Gravity;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.Locale;

final class DesktopDownloadView extends LinearLayout {
    private final TextView status,amount;private final DesktopProgressView bar;private final DesktopTheme theme;
    DesktopDownloadView(Context context){super(context);theme=new DesktopTheme(context);setOrientation(VERTICAL);LinearLayout row=theme.row();status=theme.text("다운로드 준비 중",11,DesktopTheme.MUTED);amount=theme.text("전체 크기 확인 중",10,DesktopTheme.ACCENT);amount.setGravity(Gravity.RIGHT);row.addView(status,theme.weight());row.addView(amount);addView(row);bar=new DesktopProgressView(context);LayoutParams p=new LayoutParams(-1,theme.dp(6));p.topMargin=theme.dp(7);addView(bar,p);setPadding(0,theme.dp(8),0,theme.dp(4));setContentDescription("오디오 다운로드 진행률");}
    void reset(){setVisibility(VISIBLE);status.setText("다운로드 준비 중");amount.setText("전체 크기 확인 중");amount.setTextColor(DesktopTheme.ACCENT);bar.update(0,DesktopTheme.ACCENT,false);}
    void update(ExternalMedia.Progress progress){setVisibility(VISIBLE);status.setText(progress.status);String value;
        if(progress.percent!=null){value=String.format(Locale.ROOT,"%s%.1f%%",progress.estimated?"약 ":"",Math.floor(Math.max(0,Math.min(100,progress.percent))*10)/10);if(progress.totalBytes!=null)value+=" · "+bytes(progress.downloadedBytes)+" / "+bytes(progress.totalBytes);}
        else value=progress.downloadedBytes>0?bytes(progress.downloadedBytes)+" · 전체 크기 확인 중":"전체 크기 확인 중";
        amount.setText(value);amount.setTextColor(DesktopTheme.ACCENT);bar.update(progress.percent==null?.15:progress.percent/100,DesktopTheme.ACCENT,false);}
    void failure(){status.setText("다운로드 실패");amount.setTextColor(DesktopTheme.WARM);bar.update(1,DesktopTheme.WARM,false);}
    private static String bytes(long count){return count>=1024*1024?String.format(Locale.ROOT,"%.1f MB",count/(1024d*1024)):String.format(Locale.ROOT,"%.1f KB",count/1024d);}
}
