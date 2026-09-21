package jp.virtualcd.player;

import android.app.Activity;
import android.app.Dialog;
import android.view.*;
import android.widget.*;
import jp.virtualcd.player.audio.*;
import jp.virtualcd.player.library.PlayerStyle;
import java.util.ArrayList;
import java.util.Arrays;

/** Scrollable phone-friendly controls; changes persist and apply to the running service immediately. */
final class SoundSettingsDialog {
    private final String[] MODES={"OFF",jp.virtualcd.player.LanguageStrings.text("軽め","Light"),jp.virtualcd.player.LanguageStrings.text("標準","Standard"),jp.virtualcd.player.LanguageStrings.text("強め","Strong"),jp.virtualcd.player.LanguageStrings.text("劇的（強調）","Dramatic"),jp.virtualcd.player.LanguageStrings.text("HDR風（実験）","HDR style (experimental)")};
    private final String[] PRESETS={jp.virtualcd.player.LanguageStrings.text("フラット","Flat"),jp.virtualcd.player.LanguageStrings.text("低音強調","Bass boost"),jp.virtualcd.player.LanguageStrings.text("高音強調","Treble boost"),jp.virtualcd.player.LanguageStrings.text("ボーカル","Vocal"),jp.virtualcd.player.LanguageStrings.text("ロック","Rock"),jp.virtualcd.player.LanguageStrings.text("カスタム","Custom")};
    private static final int[][] CURVES={{0,0,0,0,0,0,0,0,0,0},{6,5,4,2,0,0,0,0,0,0},{0,0,0,0,0,1,2,3,4,5},{-2,-2,-1,0,2,3,3,1,0,-1},{4,3,1,-1,-2,0,2,3,4,3}};
    private final Activity activity;
    private final SoundPreferences store;
    private final Dialog dialog;
    private final LinearLayout body;
    private final ArrayList<View> effects=new ArrayList<>();
    private CheckBox faithful,gapless,normalize,clarity,eq,bass;
    private Spinner mode,preset;
    private final SeekBar[] bands=new SeekBar[10];
    private SeekBar amount;
    private SeekBar speed,pitch;
    private boolean binding;
    static Dialog show(Activity activity){return new SoundSettingsDialog(activity).dialog;}
    private SoundSettingsDialog(Activity activity){
        this.activity=activity;store=new SoundPreferences(activity);SoundConfig initial=store.read();binding=true;
        dialog=new Dialog(activity);dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
        LinearLayout root=new LinearLayout(activity);root.setOrientation(LinearLayout.VERTICAL);root.setPadding(dp(16),dp(12),dp(16),dp(12));root.setBackground(PlayerStyle.panel(activity,0xff19212b,18));
        LinearLayout header=new LinearLayout(activity);root.addView(header);TextView title=text(jp.virtualcd.player.LanguageStrings.text("音の設定","Sound settings"),21);header.addView(title,new LinearLayout.LayoutParams(0,dp(48),1));
        Button close=new Button(activity);PlayerStyle.button(close);close.setText(jp.virtualcd.player.LanguageStrings.text("閉じる","Close"));close.setOnClickListener(v->dialog.dismiss());header.addView(close,new LinearLayout.LayoutParams(dp(64),dp(44)));
        ScrollView scroll=new ScrollView(activity);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));body=new LinearLayout(activity);body.setOrientation(LinearLayout.VERTICAL);scroll.addView(body);
        note(jp.virtualcd.player.LanguageStrings.text("再生中に反映・自動保存。元の音楽ファイルは変更しません。","Applied during playback and saved automatically. Original music files are not changed."));
        body.addView(text(jp.virtualcd.player.LanguageStrings.text("再生速度・ピッチ","Playback speed / pitch"),16));
        speed=slider(jp.virtualcd.player.LanguageStrings.text("速度","Speed"),30,(store.speedPercent()-50)/5,false,v->saveTuning(),v->String.format(java.util.Locale.ROOT,"%.2f×",(50+v*5)/100.0));
        pitch=slider(jp.virtualcd.player.LanguageStrings.text("ピッチ","Pitch"),24,store.pitchSemitones()+12,false,v->saveTuning(),v->String.format(java.util.Locale.ROOT,jp.virtualcd.player.LanguageStrings.text("%+d 半音","%+d semitones"),v-12));
        Button resetTuning=new Button(activity);PlayerStyle.button(resetTuning);resetTuning.setText(jp.virtualcd.player.LanguageStrings.text("速度・ピッチを標準に戻す","Reset speed and pitch"));body.addView(resetTuning,new LinearLayout.LayoutParams(-1,dp(44)));
        resetTuning.setOnClickListener(v->{binding=true;speed.setProgress(10);pitch.setProgress(12);binding=false;saveTuning();});
        note(jp.virtualcd.player.LanguageStrings.text("速度：0.50〜2.00倍（0.05刻み）／ピッチ：±12半音。独立して調整できます。原音忠実モードとは別設定です。標準は1.00倍・0半音です。","Speed: 0.50–2.00× in 0.05 steps. Pitch: ±12 semitones. Adjust independently of original sound mode. Defaults: 1.00× and 0 semitones."));
        gapless=check(jp.virtualcd.player.LanguageStrings.text("ギャップレス再生","Gapless playback"),store.gapless(),false);note(jp.virtualcd.player.LanguageStrings.text("ON：連続再生。OFF：曲ごとに再開します。音源内の無音は除去しません。","ON: continuous playback. OFF: restart for each track. Silence within source audio is not removed."));
        faithful=check(jp.virtualcd.player.LanguageStrings.text("原音忠実モード","Original sound mode"),initial.faithful,false);note(jp.virtualcd.player.LanguageStrings.text("ON：以下の補正をバイパス（設定値は保持）。Androidの出力経路を含むビットパーフェクトを保証する機能ではありません。","ON bypasses the enhancements below while retaining their settings. This does not guarantee bit-perfect output through Android."));
        body.addView(text(jp.virtualcd.player.LanguageStrings.text("音質向上（リアルタイム）","Sound enhancement (real-time)"),16));mode=spinner(MODES,initial.mode);effects.add(mode);
        normalize=check(jp.virtualcd.player.LanguageStrings.text("音量ノーマライズ","Volume normalization"),initial.normalize,true);note(jp.virtualcd.player.LanguageStrings.text("RMS基準で曲の音量差を緩やかに補正。LUFS解析／ReplayGainではありません。","Gently reduces track volume differences using RMS. This is not LUFS analysis or ReplayGain."));
        clarity=check(jp.virtualcd.player.LanguageStrings.text("小音量クリア","Low-volume clarity"),initial.clarity,true);
        eq=check(jp.virtualcd.player.LanguageStrings.text("10バンドEQ ON","10-band EQ ON"),initial.eq,true);
        int presetIndex=CURVES.length;for(int i=0;i<CURVES.length;i++)if(Arrays.equals(CURVES[i],initial.gains()))presetIndex=i;
        preset=spinner(PRESETS,presetIndex);effects.add(preset);
        for(int i=0;i<10;i++){final int band=i;int hz=SoundConfig.FREQUENCIES[i];String name=hz<1000?hz+" Hz":(hz/1000)+" kHz";
            bands[i]=slider(name,24,initial.gain(i)+12,true,value->{if(!binding){preset.setSelection(CURVES.length);save();}},v->String.format(java.util.Locale.ROOT,"%+d dB",v-12));}
        Button reset=new Button(activity);PlayerStyle.button(reset);reset.setText(jp.virtualcd.player.LanguageStrings.text("EQをフラットに戻す","Reset EQ to flat"));body.addView(reset,new LinearLayout.LayoutParams(-1,dp(44)));effects.add(reset);
        reset.setOnClickListener(v->{applyCurve(0);preset.setSelection(0);});
        bass=check(jp.virtualcd.player.LanguageStrings.text("EXTRA BASS風","Extra bass style"),initial.bass,true);amount=slider(jp.virtualcd.player.LanguageStrings.text("強さ","Strength"),100,initial.amount,true,v->save(),v->v+"% ");
        note(jp.virtualcd.player.LanguageStrings.text("低音強調は本アプリの処理です。Sony独自の音質処理ではありません。補正中はピーク保護が働きます。まず小さめの音量でお試しください。","Bass boost is processed by this app, not Sony proprietary processing. Peak protection is active. Try it at a low volume first."));
        mode.setOnItemSelectedListener(selection(()->save()));
        preset.setOnItemSelectedListener(selection(()->{int i=preset.getSelectedItemPosition();if(i<CURVES.length&&!Arrays.equals(CURVES[i],currentGains()))applyCurve(i);}));
        binding=false;updateEnabled();dialog.setContentView(root);dialog.show();
        Window window=dialog.getWindow();if(window!=null){window.setBackgroundDrawableResource(android.R.color.transparent);window.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
            var p=window.getAttributes();p.width=activity.getResources().getDisplayMetrics().widthPixels-dp(16);p.height=(int)(activity.getResources().getDisplayMetrics().heightPixels*.88);p.gravity=Gravity.BOTTOM;p.dimAmount=.6f;window.setAttributes(p);}
    }
    private void applyCurve(int index){binding=true;for(int i=0;i<10;i++)bands[i].setProgress(CURVES[index][i]+12);binding=false;save();}
    private void saveTuning(){if(!binding)store.saveTuning(50+speed.getProgress()*5,pitch.getProgress()-12);}
    private int[] currentGains(){int[] gains=new int[10];for(int i=0;i<10;i++)gains[i]=bands[i].getProgress()-12;return gains;}
    private void save(){if(binding)return;store.save(new SoundConfig(faithful.isChecked(),mode.getSelectedItemPosition(),normalize.isChecked(),clarity.isChecked(),eq.isChecked(),bass.isChecked(),amount.getProgress(),currentGains()),gapless.isChecked());updateEnabled();}
    private void updateEnabled(){for(View view:effects){view.setEnabled(!faithful.isChecked());view.setAlpha(faithful.isChecked()?.4f:1);}}
    private CheckBox check(String title,boolean checked,boolean effect){CheckBox box=new CheckBox(activity);box.setText(title);box.setTextColor(0xffe3edf5);box.setTextSize(15);box.setMinHeight(dp(48));box.setChecked(checked);body.addView(box);if(effect)effects.add(box);box.setOnCheckedChangeListener((v,on)->save());return box;}
    private Spinner spinner(String[] options,int selected){Spinner s=new Spinner(activity);s.setAdapter(new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,options));s.setSelection(selected);body.addView(s,new LinearLayout.LayoutParams(-1,dp(48)));return s;}
    private SeekBar slider(String name,int max,int progress,boolean effect,java.util.function.IntConsumer change,java.util.function.IntFunction<String> format){
        LinearLayout row=new LinearLayout(activity);row.setGravity(Gravity.CENTER_VERTICAL);body.addView(row,new LinearLayout.LayoutParams(-1,dp(48)));
        TextView label=text(name,13);row.addView(label,new LinearLayout.LayoutParams(dp(60),-2));SeekBar seek=new SeekBar(activity);seek.setMax(max);seek.setProgress(progress);seek.setContentDescription(name);row.addView(seek,new LinearLayout.LayoutParams(0,dp(48),1));
        TextView value=text(format.apply(progress),12);value.setGravity(Gravity.END);row.addView(value,new LinearLayout.LayoutParams(dp(56),-2));if(effect)effects.add(seek);
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar s){}public void onStopTrackingTouch(SeekBar s){}
            public void onProgressChanged(SeekBar s,int p,boolean user){value.setText(format.apply(p));if(user)change.accept(p);}});return seek;
    }
    private AdapterView.OnItemSelectedListener selection(Runnable run){return new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> p){}public void onItemSelected(AdapterView<?> p,View v,int i,long id){if(!binding)run.run();}};}
    private void note(String message){TextView t=text(message,12);t.setTextColor(0xff9cafbf);t.setPadding(0,0,0,dp(10));body.addView(t);}
    private TextView text(String value,int size){TextView t=new TextView(activity);t.setText(value);t.setTextSize(size);t.setTextColor(0xffe3edf5);t.setGravity(Gravity.CENTER_VERTICAL);return t;}
    private int dp(int value){return PlayerStyle.dp(activity,value);}
}
