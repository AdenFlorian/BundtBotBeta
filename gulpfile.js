const exec = require('child_process').exec
const copydir = require('copy-dir')
const fs = require('fs')
const gulp = require('gulp')
const shell = require('gulp-shell')
const rimraf = require('rimraf')
const shelljs = require('shelljs')
const client = require('scp2')

const secretFilePath = './secret.json'

const projectName = 'BundtBot'
const projectFolder = `src/${projectName}`
const projectFileName = `${projectName}.csproj`
const projectFilePath = `${projectFolder}/${projectFileName}`

const buildOutputFolder = `${projectFolder}/bin/debug/netcoreapp1.1`
const publishFolder = `${buildOutputFolder}/publish`

const tarFileName = `${projectName}.tar.gz`
const viewsFolderName = `Views`
const viewsFolder = `${projectFolder}/${viewsFolderName}`

const testFolder = 'test'
const rateLimitTestsProjectName = 'RateLimitTests'
const rateLimitTestsProjectFolder = `${testFolder}/${rateLimitTestsProjectName}`
const rateLimitTestsOutputFolder = `${rateLimitTestsProjectFolder}/bin/Debug/netcoreapp1.1`

var secret

if (fs.existsSync(secretFilePath)) {
	secret = JSON.parse(fs.readFileSync(secretFilePath))
	// Add all the gruntfile tasks to gulp
	require('gulp-grunt')(gulp)
} else {
	gulp.stop("***Run 'node setup.js' before using gulp!***")
}

gulp.task('clean', cleanTar)

gulp.task('restore', shell.task(`dotnet restore ${projectFilePath}`, { verbose: true }))

gulp.task('dotnet-restore', (cb) => {
	exec('dotnet restore', (error, stdout, stderr) => {
		console.log(stdout)
		cb()
	})
})

gulp.task('dotnet-build', ['dotnet-restore'], shell.task(`dotnet build ${projectFilePath}`, { verbose: true }))

gulp.task('copyviews', ['dotnet-build'], (cb) => {
	copydir(viewsFolder, `${buildOutputFolder}/${viewsFolderName}`, (err) => {
		if (err) throw err
		cb()
	})
})

gulp.task('copytokendev', ['dotnet-build'], () => {
	fs.writeFileSync(`${buildOutputFolder}/bottoken`, secret.devbottoken)
})

gulp.task('copytokentest', ['publish'], () => {
	fs.writeFileSync(`${publishFolder}/bottoken`, secret.testbottoken)
})

gulp.task('copyconfigdev', ['dotnet-build'], () => {
	fs.createReadStream('config/dev/config.json').pipe(fs.createWriteStream(buildOutputFolder + '/config.json'))
})

gulp.task('copyconfigtest', ['publish'], () => {
	fs.createReadStream('config/test/config.json').pipe(fs.createWriteStream(publishFolder + '/config.json'))
})

gulp.task('build', ['dotnet-build', 'copyviews', 'copytokendev', 'copyconfigdev'], () => {
	copywindowsbinsbuild()
})

gulp.task('run', ['build'], shell.task(`dotnet BundtBot.dll`, { verbose: true, cwd: buildOutputFolder }))

function copywindowsbinsbuild() {
	fs.createReadStream('bin/opus/windows-1.1.2-x86-64/opus.dll').pipe(fs.createWriteStream(buildOutputFolder + '/libopus.dll'))
	fs.createReadStream('bin/libsodium/windows-1.0.12-x86-64/libsodium.dll').pipe(fs.createWriteStream(buildOutputFolder + '/libsodium.dll'))
	fs.createReadStream('bin/youtube-dl/windows/youtube-dl.exe').pipe(fs.createWriteStream(buildOutputFolder + '/youtube-dl.exe'))
	fs.createReadStream('bin/ffmpeg/windows/ffmpeg.exe').pipe(fs.createWriteStream(buildOutputFolder + '/ffmpeg.exe'))
	fs.createReadStream('bin/ffmpeg/windows/ffprobe.exe').pipe(fs.createWriteStream(buildOutputFolder + '/ffprobe.exe'))
}

function copylinuxbinspublish() {
	fs.createReadStream('bin/opus/linux-1.1.2-x86-64/libopus.so.0.5.2').pipe(fs.createWriteStream(publishFolder + '/libopus.dll'))
	fs.createReadStream('bin/libsodium/linux-1.0.12-x86-64/libsodium.so.18.2.0').pipe(fs.createWriteStream(publishFolder + '/libsodium.dll'))
	fs.createReadStream('bin/youtube-dl/linux/youtube-dl.exe').pipe(fs.createWriteStream(publishFolder + '/youtube-dl.exe'))
}

gulp.task('publish', ['dotnet-restore'], (cb) => {
	exec(`dotnet publish ${projectFilePath}`, (error, stdout, stderr) => {
		console.log(stdout)
		copylinuxbinspublish()
		cb()
	})
})

gulp.task('tar', ['publish', 'copytokentest', 'copyconfigtest'], shell.task('node do tar', { verbose: true }))

gulp.task('sftpdeploy', ['tar'], sftpDeploy)

gulp.task('cleantar', cleanTar)

gulp.task('sshdeploy', ['sftpdeploy'], shell.task('grunt sshexec:deploy', { verbose: true }))

gulp.task('deploy', ['publish', 'tar', 'sftpdeploy', 'sshdeploy'], cleanTar)

// Start test commands

gulp.task('test', shell.task('dotnet test test/BundtBotTests/BundtBotTests.csproj',
	{ verbose: true }))

gulp.task('rate-limiter-tests', () => shelljs.exec(`dotnet test ${rateLimitTestsProjectFolder}/${rateLimitTestsProjectName}.csproj`))

// TesterBot
const testerBotProjectName = 'TesterBot'
const testerBotProjectFolder = 'test/' + testerBotProjectName
const testerBotProjectFile = testerBotProjectFolder + '/' + testerBotProjectName + '.csproj'
const testerBotBinFolder = testerBotProjectFolder + '/bin'
const testerBotObjFolder = testerBotProjectFolder + '/obj'
const testerBotOutputFolder = testerBotBinFolder + '/debug/netcoreapp1.1'

gulp.task('build-testerbot', (cb) => {
	exec(`dotnet build ${testerBotProjectFile}`, (error, stdout, stderr) => {
		console.log(stdout)
		if (error) throw error
		fs.writeFileSync(testerBotOutputFolder + '/bottoken', secret.testerbottoken)
		cb()
	})
})

gulp.task('clean-testerbot', (cb) => {
	rimraf(testerBotBinFolder, (err) => {
		if (err) throw err
	})
})

gulp.task('run-testerbot', ['build-testerbot'], shell.task(`dotnet ${testerBotProjectName}.dll`, { verbose: true, cwd: testerBotOutputFolder }))

// Start remote server commands

gulp.task('rlogs', shell.task(
	`ssh ${secret.testusername}@${secret.testhost} "journalctl -f -o cat -u bundtbot.service"`,
	{ verbose: true }))

gulp.task('setup-server', shell.task('grunt sshexec:setup', { verbose: true }))

gulp.task('restart-remote', shell.task('grunt sshexec:restart', { verbose: true }))

function sftpDeploy(cb)
{
	client.defaults({
		port: 22,
		host: secret.testhost,
		username: secret.testusername,
		privateKey: fs.readFileSync(secret.sshkeypath)
	})

	client.on('transfer', (buffer, uploaded, total) => {
		if (uploaded % 25 == 0) {
			console.log(uploaded + '/' + total)
		}
	})

	client.upload(tarFileName, tarFileName, () => {
		client.close()
		cb()
	})
}

function cleanTar(cb)
{
	fs.unlink(`${projectName}.tar.gz`, (err) => {
		if (err) console.log(err)
		if (cb) cb()
	})
}
